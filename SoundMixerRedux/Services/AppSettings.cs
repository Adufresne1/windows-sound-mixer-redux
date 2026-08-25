namespace SoundMixerRedux.Services;

/// <summary>
/// The only data the app persists (audio state always lives in Windows).
/// Window position and client size are physical pixels (position in virtual-screen space); both
/// null until first saved. Content scales to fit whatever size is restored, so a stale size from a
/// different track count is never a problem.
/// </summary>
public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; }
    public bool ShowDbScale { get; set; } = true;
    public bool StickToRight { get; set; }
    public bool Pinned { get; set; }

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
}
