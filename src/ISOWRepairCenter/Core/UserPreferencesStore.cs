using System.Text.Json;

namespace ISOWRepairCenter.Core;

public sealed class UserPreferencesStore
{
    private readonly string _path;

    public UserPreferencesStore(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ISOByte", "ISOWRepairCenter", "preferences.json");

    public string LoadLanguage()
    {
        try
        {
            if (!File.Exists(_path)) return "tr";
            var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path));
            return preferences?.Language is "tr" or "en" ? preferences.Language : "tr";
        }
        catch { return "tr"; }
    }

    public void SaveLanguage(string language)
    {
        if (language is not ("tr" or "en")) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new UserPreferences(language)));
        }
        catch { }
    }

    private sealed record UserPreferences(string Language);
}
