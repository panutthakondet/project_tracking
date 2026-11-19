using System.Text.Json;

namespace ProjectTracking.Services.Security;

public static class SecurityServerSettings
{
    public const string DefaultPath = @"C:\inetpub\security-data\ProjectTracking\server-settings.json";
    private static readonly string[] Keys = ["SECURITY_WORKER_KEY", "SECURITY_STORAGE_PATH", "SECURITY_AUTO_CREATE_SCHEMA"];

    // Only scanner settings are imported; existing IIS environment variables take precedence.
    public static void Load(ConfigurationManager configuration, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Dictionary<string, string?> settings;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            settings = Keys.Where(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                .Where(key => document.RootElement.TryGetProperty(key, out _))
                .ToDictionary(key => key, key => document.RootElement.GetProperty(key).GetString());
            if (settings.TryGetValue("SECURITY_WORKER_KEY", out var key) && (string.IsNullOrWhiteSpace(key) || key.Length < 32))
                throw new InvalidOperationException("Invalid worker key");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // Do not include the source JSON or credentials in startup errors.
            throw new InvalidOperationException("Security server-settings.json is invalid. Check its JSON structure and scanner settings.");
        }
        configuration.AddInMemoryCollection(settings);
    }
}
