using Microsoft.Windows.ApplicationModel.Resources;

namespace SoundMixerRedux.Services;

/// <summary>
/// Thin wrapper around MRT Core's ResourceLoader for the handful of strings set from code
/// rather than XAML (x:Uid covers everything else).
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);
}
