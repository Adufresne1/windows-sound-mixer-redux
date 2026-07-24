using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

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

    /// <summary>False while another channel is soloed: you can't mute/unmute the non-soloed channels
    /// (Solo owns their mute state), but Solo itself stays available for A/B transfer.</summary>
    [ObservableProperty]
    private bool _muteEnabled = true;

    /// <summary>Greyed out because another channel in the same section is soloed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotDimmed))]
    [NotifyPropertyChangedFor(nameof(StripOpacity))]
    private bool _isDimmed;

    public bool NotDimmed => !IsDimmed;

    /// <summary>Dimmed strips are faded but stay interactive (so Solo can be transferred / a channel unmuted).</summary>
    public double StripOpacity => IsDimmed ? 0.5 : 1.0;

    /// <summary>Current VU level 0..100 (real peak in dBFS-mapped %, Phase 4).</summary>
    [ObservableProperty]
    private double _peak;

    /// <summary>Render the dB graduation scale beside this strip's meter (set on the Master strip only).</summary>
    [ObservableProperty]
    private bool _showScale;

    public string Name { get; set; } = string.Empty;

    public bool IsMaster { get; set; }

    /// <summary>Two-letter initials shown on the app tile (mock stand-in for the real app icon).</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>Segoe Fluent Icons glyph used for Master channels (Volume / Microphone).</summary>
    public string? Glyph { get; set; }

    /// <summary>Tile background colour (hex), approximating the app's brand colour in the mockups.</summary>
    public string TileColor { get; set; } = "#3A7BD5";

    /// <summary>Real app icon extracted from the session's process (null → fall back to initials).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIcon))]
    [NotifyPropertyChangedFor(nameof(ShowInitials))]
    private ImageSource? _iconImage;

    // Tile content selection: Master → glyph, session with icon → image, otherwise → initials.
    public bool ShowGlyph => IsMaster;
    public bool ShowIcon => !IsMaster && IconImage != null;
    public bool ShowInitials => !IsMaster && IconImage == null;

    /// <summary>Master (endpoint) channels have no Solo — a mix always needs a source.</summary>
    public bool ShowSolo => !IsMaster;

    // Muted → fader at −∞ dB; otherwise the volume percentage (unit folded in).
    public string VolumeLabel => IsMuted ? "−∞" : $"{(int)Math.Round(Volume)}%";
}
