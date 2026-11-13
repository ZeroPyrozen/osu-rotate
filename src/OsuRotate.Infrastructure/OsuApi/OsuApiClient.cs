using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OsuRotate.Infrastructure.OsuApi;

public sealed class OsuApiOptions
{
    public string BaseUrl { get; init; } = "https://osu.ppy.sh";
    public int ClientId { get; init; }
    public string ClientSecret { get; init; } = string.Empty;

    // osu! asks for no more than 60 a minute. Half leaves room for the dashboard and for retries.
    public int MaxRequestsPerMinute { get; init; } = 30;
    public int Retries { get; init; } = 2;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsConfigured => ClientId > 0 && ClientSecret.Length > 0;
}

internal enum ApiOutcome
{
    Ok,
    NotFound,
    Failed,
}

internal sealed record ApiResult(ApiOutcome Outcome, JsonDocument? Body);

// osu! API v2 with a client-credentials token. Spaces requests out to stay under the rate limit,
// retries server errors and 429s, renews the token on a 401, and never throws for an HTTP or network
// failure: the caller gets Failed. The client secret is never logged.
public sealed class OsuApiClient : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly OsuApiOptions _options;
    private readonly ILogger<OsuApiClient> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recent = new();
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public OsuApiClient(HttpClient http, OsuApiOptions options, ILogger<OsuApiClient> logger, TimeProvider? time = null)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public void Dispose() => _gate.Dispose();

    internal async Task<ApiResult> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return new ApiResult(ApiOutcome.Failed, null);
        }

        for (var attempt = 0; ; attempt++)
        {
            var last = attempt >= _options.Retries;
            TimeSpan? wait;
            try
            {
                var token = await TokenAsync(cancellationToken).ConfigureAwait(false);
                if (token is null)
                {
                    return new ApiResult(ApiOutcome.Failed, null);
                }

                await ThrottleAsync(cancellationToken).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.BaseUrl), path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        await using (stream.ConfigureAwait(false))
                        {
                            return new ApiResult(ApiOutcome.Ok, await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false));
                        }

                    case HttpStatusCode.NotFound:
                        return new ApiResult(ApiOutcome.NotFound, null);

                    case HttpStatusCode.Unauthorized:
                        _token = null;
                        wait = TimeSpan.Zero;
                        break;

                    case HttpStatusCode.TooManyRequests:
                        wait = RetryAfter(response) ?? _options.RetryDelay * (attempt + 1);
                        break;

                    case >= HttpStatusCode.InternalServerError:
                        wait = _options.RetryDelay * (attempt + 1);
                        break;

                    default:
                        _logger.LogWarning("osu! API answered {Status} for {Path}.", (int)response.StatusCode, path);
                        return new ApiResult(ApiOutcome.Failed, null);
                }

                _logger.LogInformation("osu! API answered {Status} for {Path}.", (int)response.StatusCode, path);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException or KeyNotFoundException or InvalidOperationException)
            {
                _logger.LogWarning("osu! API request for {Path} failed: {Message}", path, exception.Message);
                wait = _options.RetryDelay * (attempt + 1);
            }

            if (last || wait > _options.MaxRetryAfter)
            {
                return new ApiResult(ApiOutcome.Failed, null);
            }

            await Task.Delay(wait.Value, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    // Never negative: a Retry-After date that has already passed means "now", and Task.Delay throws
    // on a negative wait.
    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        TimeSpan? wait = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - _time.GetUtcNow(),
            _ => null,
        };
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    private async Task<string?> TokenAsync(CancellationToken cancellationToken)
    {
        if (_token is { } current && _time.GetUtcNow() < _tokenExpires)
        {
            return current;
        }

        await ThrottleAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _http.PostAsJsonAsync(
            new Uri(new Uri(_options.BaseUrl), "/oauth/token"),
            new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("The osu! API refused a token: HTTP {Status}. Check the client id and secret.", (int)response.StatusCode);
            return null;
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var token = body.RootElement.GetProperty("access_token").GetString();
        var lifetime = TimeSpan.FromSeconds(body.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600);

        // Renewed a minute early so a request never goes out with a token about to lapse.
        _tokenExpires = _time.GetUtcNow() + lifetime - TimeSpan.FromMinutes(1);
        _token = token;
        return token;
    }

    // A sliding window: no more than MaxRequestsPerMinute requests in any minute.
    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = _time.GetUtcNow();
                while (_recent.TryPeek(out var oldest) && now - oldest >= Window)
                {
                    _recent.Dequeue();
                }

                if (_recent.Count < _options.MaxRequestsPerMinute)
                {
                    _recent.Enqueue(now);
                    return;
                }

                await Task.Delay(_recent.Peek() + Window - now, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
