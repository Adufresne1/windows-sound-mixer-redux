using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace SoundMixerRedux.ViewModels;

/// <summary>
/// One mixer channel (Master endpoint or a per-app session).
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

    /// <summary>Multiplier applied to every fixed dimension in ChannelStrip.xaml, kept in sync with the
    /// window size by MixerViewModel.BoardScale (real layout, not a RenderTransform).</summary>
    [ObservableProperty]
    private double _scale = 1.0;

    /// <summary>Pixel height of the VU meter's "unfilled" mask. Depends on both Peak and Scale (the
    /// meter's real height is 180 * Scale, not a fixed 180) — computed here instead of a XAML converter
    /// because a converter only sees one bound value and can't combine the two.</summary>
    [ObservableProperty]
    private double _meterMaskHeight = 180;

    partial void OnPeakChanged(double value) => RecomputeMeterMaskHeight();
    partial void OnScaleChanged(double value) => RecomputeMeterMaskHeight();

    private void RecomputeMeterMaskHeight()
    {
        MeterMaskHeight = (1.0 - Math.Clamp(Peak, 0, 100) / 100.0) * 180.0 * Scale;
    }

    /// <summary>Persisted per-process choice to collapse this strip from the normal view (Phase E).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInList))]
    [NotifyPropertyChangedFor(nameof(ShowHiddenBadge))]
    private bool _isHidden;

    /// <summary>Pushed down by MixerViewModel while the "manage hidden tracks" mode is active — forces
    /// every strip (including hidden ones) visible so the user can toggle them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInList))]
    [NotifyPropertyChangedFor(nameof(CanToggleHidden))]
    [NotifyPropertyChangedFor(nameof(ShowHiddenBadge))]
    private bool _selectionModeActive;

    public bool ShowInList => !IsHidden || SelectionModeActive;

    /// <summary>Master endpoints are never hideable.</summary>
    public bool CanToggleHidden => SelectionModeActive && !IsMaster;

    public bool ShowHiddenBadge => IsHidden && SelectionModeActive;

    /// <summary>Can change after creation: some sessions set their explicit display name shortly
    /// after being created rather than before (see ResolveChannelName/RefreshChannelName).</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    public bool IsMaster { get; set; }

    /// <summary>The Windows System Sounds session — shown with a fixed glyph like Master, never a process icon.</summary>
    public bool IsSystemSounds { get; set; }

    /// <summary>Two-letter initials shown on the app tile (mock stand-in for the real app icon).</summary>
    [ObservableProperty]
    private string _initials = string.Empty;

    /// <summary>Segoe Fluent Icons glyph used for Master channels (Volume / Microphone).</summary>
    public string? Glyph { get; set; }

    /// <summary>Tile background colour (hex), approximating the app's brand colour in the mockups.</summary>
    [ObservableProperty]
    private string _tileColor = "#3A7BD5";

    /// <summary>Real app icon extracted from the session's process (null → fall back to initials).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIcon))]
    [NotifyPropertyChangedFor(nameof(ShowInitials))]
    private ImageSource? _iconImage;

    // Tile content selection: Master/System Sounds → glyph, session with icon → image, otherwise → initials.
    public bool ShowGlyph => IsMaster || IsSystemSounds;
    public bool ShowIcon => !IsMaster && !IsSystemSounds && IconImage != null;
    public bool ShowInitials => !IsMaster && !IsSystemSounds && IconImage == null;

    /// <summary>Master (endpoint) channels have no Solo — a mix always needs a source.</summary>
    public bool ShowSolo => !IsMaster;

    // Muted → fader at −∞ dB; otherwise the volume percentage (unit folded in).
    public string VolumeLabel => IsMuted ? "−∞" : $"{(int)Math.Round(Volume)}%";
}
