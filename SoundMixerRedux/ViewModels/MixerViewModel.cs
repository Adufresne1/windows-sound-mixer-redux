using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NAudio.CoreAudioApi;
using SoundMixerRedux.Services;

namespace SoundMixerRedux.ViewModels;

/// <summary>
/// Root view model for the mixer.
/// Phase 2: Master output/input wired to the default endpoints.
/// Phase 3: per-app output channels are backed by real Core Audio sessions
/// (volume/mute both ways + external reflect, dynamic add/remove).
/// VU meters remain ambient until Phase 4.
/// </summary>
public partial class MixerViewModel : ObservableObject
{
    private static readonly string[] Palette =
    {
        "#3A7BD5", "#1DB954", "#4285F4", "#5865F2", "#7B2FF7",
        "#E8590C", "#0EA5E9", "#DB2777", "#059669", "#D97706",
    };

    private readonly DispatcherTimer _vuTimer;
    private readonly AudioService? _audio;
    private readonly ChannelViewModel _masterOutput;
    private readonly ChannelViewModel _masterInput;

    // Real session channels: maps in both directions + the per-session external-change handlers.
    private readonly Dictionary<ChannelViewModel, IAudioControl> _controlByChannel = new();
    private readonly Dictionary<AudioSessionGroup, ChannelViewModel> _channelByGroup = new();
    private readonly Dictionary<AudioSessionGroup, Action> _groupHandlers = new();

    // Solo: mute states captured before a solo engaged, restored when it releases (per section).
    private readonly Dictionary<ChannelViewModel, bool> _priorMutesOut = new();
    private readonly Dictionary<ChannelViewModel, bool> _priorMutesIn = new();

    /// <summary>True while Solo drives mutes, so those changes aren't misread as manual mutes.</summary>
    private bool _applyingSolo;

    /// <summary>True while syncing the selection to an external/default change, to avoid re-setting the default.</summary>
    private bool _syncingDevices;

    /// <summary>True while pushing a Windows-originated change into a VM, to avoid writing it straight back.</summary>
    private bool _applyingExternal;

    public ObservableCollection<ChannelViewModel> Outputs { get; } = new();
    public ObservableCollection<ChannelViewModel> Inputs { get; } = new();

    public List<AudioDeviceInfo> OutputDevices { get; private set; } = new();
    public List<AudioDeviceInfo> InputDevices { get; private set; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedInputDevice;

    // Settings (mock, visual only until Phase 7).
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _showDbScale = true;

    public MixerViewModel()
    {
        ShowDbScale = SettingsService.Current.ShowDbScale;
        AlwaysOnTop = SettingsService.Current.AlwaysOnTop;

        _masterOutput = new ChannelViewModel { Name = "Système", IsMaster = true, Volume = 80, Glyph = char.ConvertFromUtf32(0xE767), TileColor = "#3A7BD5" };
        _masterInput = new ChannelViewModel { Name = "Microphone", IsMaster = true, Volume = 70, Glyph = char.ConvertFromUtf32(0xE720), TileColor = "#1F9D6B" };

        AddChannel(_masterOutput, Outputs);
        AddChannel(_masterInput, Inputs);

        _masterOutput.ShowScale = ShowDbScale;
        _masterInput.ShowScale = ShowDbScale;

        var ui = DispatcherQueue.GetForCurrentThread();
        if (ui != null)
        {
            try
            {
                _audio = new AudioService(ui);
                _audio.Output.ExternalChange += () => PullControl(_audio.Output, _masterOutput);
                _audio.Input.ExternalChange += () => PullControl(_audio.Input, _masterInput);
                _audio.OutputGroupsChanged += ReconcileOutputSessions;
                _audio.DevicesChanged += OnDevicesChanged;
                _audio.DefaultDeviceChanged += OnDefaultDeviceChanged;

                OutputDevices = _audio.GetDevices(DataFlow.Render);
                InputDevices = _audio.GetDevices(DataFlow.Capture);

                var defOut = _audio.GetDefaultDeviceId(DataFlow.Render);
                var defIn = _audio.GetDefaultDeviceId(DataFlow.Capture);
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == defOut) ?? OutputDevices.FirstOrDefault();
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == defIn) ?? InputDevices.FirstOrDefault();
            }
            catch
            {
                _audio = null;
            }
        }

        _vuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _vuTimer.Tick += (_, _) => TickVu();
        _vuTimer.Start();
    }

    private void AddChannel(ChannelViewModel ch, ObservableCollection<ChannelViewModel> section)
    {
        ch.PropertyChanged += OnChannelPropertyChanged;
        section.Add(ch);
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_audio == null || value == null) return;
        try
        {
            // Picking a device switches the real Windows default (unless we're just syncing to an external change).
            if (!_syncingDevices && value.Id != _audio.GetDefaultDeviceId(DataFlow.Render))
                TrySetDefault(value.Id);

            _audio.SetOutputDevice(value.Id);   // re-attaches endpoint + rebuilds session groups
            PullControl(_audio.Output, _masterOutput);
        }
        catch { /* device vanished between enumeration and attach */ }
    }

    partial void OnSelectedInputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_audio == null || value == null) return;
        try
        {
            if (!_syncingDevices && value.Id != _audio.GetDefaultDeviceId(DataFlow.Capture))
                TrySetDefault(value.Id);

            _audio.SetInputDevice(value.Id);
            PullControl(_audio.Input, _masterInput);
        }
        catch { }
    }

    /// <summary>Best-effort default switch: a failure (e.g. IPolicyConfig blocked on a locked-down Windows)
    /// must not stop the selector from re-targeting the mixer.</summary>
    private void TrySetDefault(string id)
    {
        try { _audio!.SetDefaultDevice(id); }
        catch { /* fall back to re-targeting only */ }
    }

    /// <summary>Windows default changed (by us or another app) → follow it in the selector.</summary>
    private void OnDefaultDeviceChanged(DataFlow flow)
    {
        if (_audio == null) return;
        _syncingDevices = true;
        try
        {
            if (flow == DataFlow.Render)
            {
                var match = OutputDevices.FirstOrDefault(d => d.Id == _audio.GetDefaultDeviceId(DataFlow.Render));
                if (match != null && match != SelectedOutputDevice) SelectedOutputDevice = match;
            }
            else if (flow == DataFlow.Capture)
            {
                var match = InputDevices.FirstOrDefault(d => d.Id == _audio.GetDefaultDeviceId(DataFlow.Capture));
                if (match != null && match != SelectedInputDevice) SelectedInputDevice = match;
            }
        }
        finally { _syncingDevices = false; }
    }

    /// <summary>A device was plugged/unplugged or changed state → refresh the lists, keeping the selection.</summary>
    private void OnDevicesChanged()
    {
        if (_audio == null) return;
        string? outId = SelectedOutputDevice?.Id;
        string? inId = SelectedInputDevice?.Id;

        OutputDevices = _audio.GetDevices(DataFlow.Render);
        InputDevices = _audio.GetDevices(DataFlow.Capture);
        OnPropertyChanged(nameof(OutputDevices));
        OnPropertyChanged(nameof(InputDevices));

        _syncingDevices = true;
        try
        {
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == outId)
                ?? OutputDevices.FirstOrDefault(d => d.Id == _audio.GetDefaultDeviceId(DataFlow.Render))
                ?? OutputDevices.FirstOrDefault();
            SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == inId)
                ?? InputDevices.FirstOrDefault(d => d.Id == _audio.GetDefaultDeviceId(DataFlow.Capture))
                ?? InputDevices.FirstOrDefault();
        }
        finally { _syncingDevices = false; }
    }

    partial void OnShowDbScaleChanged(bool value)
    {
        foreach (var ch in Outputs) ch.ShowScale = value;
        foreach (var ch in Inputs) ch.ShowScale = value;

        SettingsService.Current.ShowDbScale = value;
        SettingsService.Save();
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        SettingsService.Current.AlwaysOnTop = value;
        SettingsService.Save();
    }

    // ---- Session channels (add/remove reconciliation) ----

    private void ReconcileOutputSessions()
    {
        if (_audio == null) return;
        var current = _audio.OutputGroups;

        foreach (var g in _channelByGroup.Keys.Where(x => !current.Contains(x)).ToList())
            RemoveSessionChannel(g);

        foreach (var g in current)
            if (!_channelByGroup.ContainsKey(g))
                AddSessionChannel(g);
    }

    private void AddSessionChannel(AudioSessionGroup group)
    {
        var (name, iconPid) = ProcessNaming.Resolve(group.ProcessId, group.IsSystemSounds);
        var ch = new ChannelViewModel { Name = name, Initials = InitialsOf(name), TileColor = ColorFor(name) };

        // Seed from Windows before wiring the handler so it doesn't echo straight back.
        try { ch.Volume = group.VolumeScalar * 100.0; ch.IsMuted = group.Mute; } catch { }
        ch.PropertyChanged += OnChannelPropertyChanged;

        ch.IconImage = TryLoadIcon(iconPid);
        ch.ShowScale = ShowDbScale;

        Action handler = () => PullControl(group, ch);
        _controlByChannel[ch] = group;
        _channelByGroup[group] = ch;
        _groupHandlers[group] = handler;
        group.ExternalChange += handler;

        Outputs.Add(ch);

        // If a solo is active in this section, fold the newcomer in (muted + dimmed + mute locked).
        if (Outputs.Any(c => c.IsSoloed) && !ch.IsMaster)
        {
            _priorMutesOut[ch] = ch.IsMuted;
            _applyingSolo = true;
            try { ch.IsMuted = true; } finally { _applyingSolo = false; }
            ch.IsDimmed = true;
            ch.MuteEnabled = false;
        }
    }

    private void RemoveSessionChannel(AudioSessionGroup group)
    {
        if (!_channelByGroup.TryGetValue(group, out var ch)) return;

        if (_groupHandlers.TryGetValue(group, out var handler))
        {
            group.ExternalChange -= handler;
            _groupHandlers.Remove(group);
        }
        ch.PropertyChanged -= OnChannelPropertyChanged;
        _controlByChannel.Remove(ch);
        _channelByGroup.Remove(group);
        _priorMutesOut.Remove(ch);
        Outputs.Remove(ch);
    }

    // ---- Volume/mute plumbing ----

    private IAudioControl? ControlFor(ChannelViewModel ch)
    {
        if (_audio == null) return null;
        if (ch == _masterOutput) return _audio.Output;
        if (ch == _masterInput) return _audio.Input;
        return _controlByChannel.TryGetValue(ch, out var control) ? control : null;
    }

    private void PullControl(IAudioControl control, ChannelViewModel ch)
    {
        _applyingExternal = true;
        try
        {
            ch.Volume = control.VolumeScalar * 100.0;
            ch.IsMuted = control.Mute;
        }
        catch { }
        finally { _applyingExternal = false; }
    }

    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChannelViewModel changed)
            return;

        if (e.PropertyName == nameof(ChannelViewModel.IsSoloed))
        {
            ApplySolo(changed);
            return;
        }

        if (_applyingExternal)
            return;

        var control = ControlFor(changed);
        if (control == null)
            return;

        try
        {
            if (e.PropertyName == nameof(ChannelViewModel.Volume))
            {
                if (changed.IsMuted)
                    changed.IsMuted = false;   // moving the fader unmutes (Windows behaviour)
                control.VolumeScalar = (float)(changed.Volume / 100.0);
            }
            else if (e.PropertyName == nameof(ChannelViewModel.IsMuted))
            {
                control.Mute = changed.IsMuted;
                if (!_applyingSolo && changed.IsMuted && changed.IsSoloed)
                {
                    // Manually muting the soloed channel cancels its solo (Q2) — and it stays muted,
                    // so drop it from the restore map before releasing.
                    (Outputs.Contains(changed) ? _priorMutesOut : _priorMutesIn).Remove(changed);
                    changed.IsSoloed = false;
                }
            }
        }
        catch { /* session/device may have vanished */ }
    }

    /// <summary>
    /// Solo (Phase 5): exclusive within a section (radio). Mutes every other channel in the section
    /// via Windows — except the Master endpoint, which must stay audible for the soloed app to be heard.
    /// Prior mute states are captured on engage and restored on release.
    /// </summary>
    private void ApplySolo(ChannelViewModel changed)
    {
        var section = Outputs.Contains(changed) ? Outputs : Inputs;
        var prior = ReferenceEquals(section, Outputs) ? _priorMutesOut : _priorMutesIn;

        // Radio: only one soloed channel per section.
        if (changed.IsSoloed)
            foreach (var ch in section)
                if (ch != changed) ch.IsSoloed = false;

        var soloed = section.FirstOrDefault(c => c.IsSoloed);

        _applyingSolo = true;
        try
        {
            if (soloed != null)
            {
                // Capture prior mute states once, when solo first engages for this section.
                if (prior.Count == 0)
                    foreach (var ch in section)
                        if (!ch.IsMaster)
                            prior[ch] = ch.IsMuted;

                // Mute everything except the soloed channel and the Master endpoint.
                foreach (var ch in section)
                    if (!ch.IsMaster)
                        ch.IsMuted = ch != soloed;
            }
            else
            {
                // Solo released → restore captured mutes.
                foreach (var kv in prior)
                    kv.Key.IsMuted = kv.Value;
                prior.Clear();
            }
        }
        finally { _applyingSolo = false; }

        // Dim + lock mute on non-soloed channels; Solo stays available for A/B transfer.
        bool anySolo = soloed != null;
        foreach (var ch in section)
        {
            ch.IsDimmed = anySolo && !ch.IsSoloed && !ch.IsMaster;
            ch.MuteEnabled = !anySolo || ch.IsMaster || ch.IsSoloed;
        }
    }

    // ---- Real VU metering (IAudioMeterInformation → dBFS) ----

    private void TickVu()
    {
        TickSection(Outputs);
        TickSection(Inputs);
    }

    private void TickSection(ObservableCollection<ChannelViewModel> section)
    {
        bool anySolo = section.Any(c => c.IsSoloed);
        foreach (var ch in section)
        {
            bool active = !ch.IsMuted && (!anySolo || ch.IsSoloed);
            // Post-fader: reflect the level leaving the mixer for this channel = raw peak × fader gain.
            float raw = active ? (ControlFor(ch)?.Peak ?? 0f) : 0f;
            double target = PeakToMeterPercent(raw * (float)(ch.Volume / 100.0));

            // Instant rise, smooth fall (light decay) for a natural-looking meter.
            ch.Peak = target >= ch.Peak ? target : ch.Peak * 0.75 + target * 0.25;
        }
    }

    private static double PeakToMeterPercent(float linear)
    {
        if (linear <= 1e-5f) return 0;
        double db = 20 * Math.Log10(linear);       // dBFS: 0 = full scale
        const double dbFloor = -48;
        double pct = (db - dbFloor) / (0 - dbFloor) * 100.0;
        return Math.Clamp(pct, 0, 100);
    }

    // ---- Session name / tile helpers ----

    private static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        var s = parts.Length == 1 ? parts[0] : name;
        if (string.IsNullOrEmpty(s)) return "?";
        return s.Length >= 2 ? char.ToUpperInvariant(s[0]) + s.Substring(1, 1) : s.ToUpperInvariant();
    }

    private static string ColorFor(string name)
    {
        int h = 0;
        foreach (char c in name) h = (h * 31 + c) & 0x7FFFFFFF;
        return Palette[h % Palette.Length];
    }

    /// <summary>Extract the app icon from the session's executable; null if unavailable (→ initials fallback).</summary>
    private static ImageSource? TryLoadIcon(uint pid)
    {
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            string? exe = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon == null) return null;

            string dir = Path.Combine(Path.GetTempPath(), "SoundMixerRedux", "icons");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, pid + ".png");
            using (var bmp = icon.ToBitmap())
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);

            return new BitmapImage(new Uri(file));
        }
        catch
        {
            return null;
        }
    }
}
