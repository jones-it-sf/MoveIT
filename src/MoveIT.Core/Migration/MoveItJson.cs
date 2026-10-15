using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoveIT.Core.Migration;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MigrationConfig))]
[JsonSerializable(typeof(MigrationManifest))]
internal sealed partial class MoveItJsonContext : JsonSerializerContext;

public sealed class ConfigException(string message) : Exception(message);

public static class MigrationConfigLoader
{
    public static MigrationConfig Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Cannot read config file '{path}': {e.Message}");
        }

        return Parse(json);
    }

    public static MigrationConfig Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, MoveItJsonContext.Default.MigrationConfig)
                ?? throw new ConfigException("The config file is empty.");
        }
        catch (JsonException e)
        {
            throw new ConfigException($"The config file is not valid: {e.Message}");
        }
    }
}
