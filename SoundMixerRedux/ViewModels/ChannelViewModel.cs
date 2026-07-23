using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundMixerRedux.ViewModels;

/// <summary>
/// One mixer channel (Master endpoint or a per-app session).
/// Phase 1: mock data only — no audio backend behind these properties yet.
/// </summary>
public partial class ChannelViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private double _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSoloed;

    /// <summary>Greyed out because another channel in the same section is soloed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotDimmed))]
    private bool _isDimmed;

    /// <summary>Convenience for XAML: bind IsEnabled to disable (grey out) a dimmed strip.</summary>
    public bool NotDimmed => !IsDimmed;

    /// <summary>Current VU level 0..100 (mock, ambient animation in Phase 1).</summary>
    [ObservableProperty]
    private double _peak;

    public string Name { get; set; } = string.Empty;

    public bool IsMaster { get; set; }

    /// <summary>Two-letter initials shown on the app tile (mock stand-in for the real app icon).</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>Segoe Fluent Icons glyph used for Master channels (Volume / Microphone).</summary>
    public string? Glyph { get; set; }

    /// <summary>Tile background colour (hex), approximating the app's brand colour in the mockups.</summary>
    public string TileColor { get; set; } = "#3A7BD5";

    public string VolumeLabel => IsMuted ? "—" : ((int)Math.Round(Volume)).ToString();
}
