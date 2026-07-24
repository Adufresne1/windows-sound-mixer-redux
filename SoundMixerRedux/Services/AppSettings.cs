namespace SoundMixerRedux.Services;

/// <summary>
/// The only data the app persists (audio state always lives in Windows).
/// Window bounds are physical pixels in virtual-screen space; null until first saved.
/// </summary>
public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; }
    public bool ShowDbScale { get; set; } = true;

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
}
