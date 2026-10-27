using System.Text.Json;
using NIVEX.Application;

namespace NIVEX.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Settings)) return new UserSettings();
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(AppPaths.Settings), Json) ?? new UserSettings();
            settings.MetricsIntervalSeconds = Math.Clamp(settings.MetricsIntervalSeconds, 2, 30);
            if (settings.Language is not ("ko" or "en")) settings.Language = "ko";
            if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                settings.ApiBaseUrl = "http://127.0.0.1:8787";
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(AppPaths.Root);
        settings.MetricsIntervalSeconds = Math.Clamp(settings.MetricsIntervalSeconds, 2, 30);
        File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(settings, Json));
    }
}
