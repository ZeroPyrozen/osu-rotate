using System.Text.Json;
using System.Text.Json.Serialization;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Persistence;

// The owner's rules as one JSON column, so adding a setting needs no migration. Enums are written by
// name so the stored file stays readable and survives reordering.
internal static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(LobbySettings settings) => JsonSerializer.Serialize(settings, Options);

    // Null when the stored text cannot be read, for example after a setting changed type.
    public static LobbySettings? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LobbySettings>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
