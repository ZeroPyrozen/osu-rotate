using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OsuRotate.Web.Tests;

public class HostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Home_page_shows_status_from_the_inner_layers()
    {
        var html = await factory.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Lobby status: <strong>Idle</strong>", html, StringComparison.Ordinal);
    }
}
