using System;
using System.IO;
using System.Text.Json;

namespace SoundMixerRedux.Services;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%\SoundMixerRedux (unpackaged app).</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly string DirPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundMixerRedux");

    private static readonly string FilePath = Path.Combine(DirPath, "settings.json");

    /// <summary>The single in-memory settings instance; mutate its properties then call <see cref="Save"/>.</summary>
    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt/unreadable → start fresh */ }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Options));
        }
        catch { /* best-effort; never crash on a settings write */ }
    }
}
