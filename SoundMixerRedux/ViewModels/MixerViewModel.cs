using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NAudio.CoreAudioApi;
using SoundMixerRedux.Services;

namespace SoundMixerRedux.ViewModels;

/// <summary>
/// Root view model for the mixer.
/// Phase 2: the Master output/input channels are wired to the real default Core Audio
/// endpoints (volume + mute, both ways, reflecting external changes). Per-app session
/// channels remain mock until Phase 3; VU meters are still ambient until Phase 4.
/// </summary>
public partial class MixerViewModel : ObservableObject
{
    private readonly DispatcherTimer _vuTimer;
    private readonly AudioService? _audio;
    private readonly ChannelViewModel _masterOutput;
    private readonly ChannelViewModel _masterInput;

    /// <summary>True while we push a Windows-originated change into a VM, to avoid writing it straight back.</summary>
    private bool _applyingExternal;

    public ObservableCollection<ChannelViewModel> Outputs { get; } = new();
    public ObservableCollection<ChannelViewModel> Inputs { get; } = new();

    public List<AudioDeviceInfo> OutputDevices { get; private set; } = new();
    public List<AudioDeviceInfo> InputDevices { get; private set; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedInputDevice;

    // Settings (mock, visual only in Phase 1/2 — persisted for real in Phase 7).
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _showDbScale = true;

    public MixerViewModel()
    {
        _masterOutput = new ChannelViewModel { Name = "Système", IsMaster = true, Volume = 80, Glyph = char.ConvertFromUtf32(0xE767), TileColor = "#3A7BD5" };
        _masterInput = new ChannelViewModel { Name = "Microphone", IsMaster = true, Volume = 70, Glyph = char.ConvertFromUtf32(0xE720), TileColor = "#1F9D6B" };

        AddOutput(_masterOutput);
        AddOutput(new ChannelViewModel { Name = "Spotify", Volume = 65, Initials = "Sp", TileColor = "#1DB954" });
        AddOutput(new ChannelViewModel { Name = "Chrome", Volume = 100, Initials = "Ch", TileColor = "#4285F4" });
        AddOutput(new ChannelViewModel { Name = "Discord", Volume = 72, Initials = "Dc", TileColor = "#5865F2" });
        AddOutput(new ChannelViewModel { Name = "Forza", Volume = 55, Initials = "Fz", TileColor = "#7B2FF7" });

        AddInput(_masterInput);
        AddInput(new ChannelViewModel { Name = "Discord", Volume = 85, Initials = "Dc", TileColor = "#5865F2" });

        // Wire the real audio backend for the Master channels.
        var ui = DispatcherQueue.GetForCurrentThread();
        if (ui != null)
        {
            try
            {
                _audio = new AudioService(ui);
                _audio.Output.ExternalChange += OnOutputExternalChange;
                _audio.Input.ExternalChange += OnInputExternalChange;

                OutputDevices = _audio.GetDevices(DataFlow.Render);
                InputDevices = _audio.GetDevices(DataFlow.Capture);

                var defOut = _audio.GetDefaultDeviceId(DataFlow.Render);
                var defIn = _audio.GetDefaultDeviceId(DataFlow.Capture);
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == defOut) ?? OutputDevices.FirstOrDefault();
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == defIn) ?? InputDevices.FirstOrDefault();
            }
            catch
            {
                // No audio endpoint / access issue: keep the mock master values, stay functional.
                _audio = null;
            }
        }

        _vuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _vuTimer.Tick += (_, _) => TickVu();
        _vuTimer.Start();
    }

    private void AddOutput(ChannelViewModel ch)
    {
        ch.PropertyChanged += OnChannelPropertyChanged;
        Outputs.Add(ch);
    }

    private void AddInput(ChannelViewModel ch)
    {
        ch.PropertyChanged += OnChannelPropertyChanged;
        Inputs.Add(ch);
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
        => AttachEndpoint(_audio?.Output, value, _masterOutput);

    partial void OnSelectedInputDeviceChanged(AudioDeviceInfo? value)
        => AttachEndpoint(_audio?.Input, value, _masterInput);

    /// <summary>Point a controller at the selected device and load its current volume/mute into the master VM.</summary>
    private void AttachEndpoint(AudioEndpointController? controller, AudioDeviceInfo? device, ChannelViewModel master)
    {
        if (_audio == null || controller == null || device == null)
            return;

        try
        {
            controller.Attach(_audio.GetDevice(device.Id));
            _applyingExternal = true;
            master.Volume = controller.VolumeScalar * 100.0;
            master.IsMuted = controller.Mute;
        }
        catch
        {
            // Device vanished between enumeration and attach — ignore, master stays at last values.
        }
        finally
        {
            _applyingExternal = false;
        }
    }

    private void OnOutputExternalChange() => PullFromEndpoint(_audio?.Output, _masterOutput);

    private void OnInputExternalChange() => PullFromEndpoint(_audio?.Input, _masterInput);

    private void PullFromEndpoint(AudioEndpointController? controller, ChannelViewModel master)
    {
        if (controller is not { HasDevice: true })
            return;

        _applyingExternal = true;
        try
        {
            master.Volume = controller.VolumeScalar * 100.0;
            master.IsMuted = controller.Mute;
        }
        finally
        {
            _applyingExternal = false;
        }
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

        // Push user-originated Master changes to Windows (skip echoes of external changes).
        if (_applyingExternal || _audio == null)
            return;

        AudioEndpointController? controller =
            changed == _masterOutput ? _audio.Output :
            changed == _masterInput ? _audio.Input : null;

        if (controller is not { HasDevice: true })
            return;

        if (e.PropertyName == nameof(ChannelViewModel.Volume))
        {
            if (changed.IsMuted)
                changed.IsMuted = false;   // moving the fader unmutes (Windows behaviour)
            controller.VolumeScalar = (float)(changed.Volume / 100.0);
        }
        else if (e.PropertyName == nameof(ChannelViewModel.IsMuted))
        {
            controller.Mute = changed.IsMuted;
        }
    }

    /// <summary>
    /// Phase 1/2 solo preview: exclusive within a section (radio) + dims the others.
    /// Full solo logic (Windows mute + prior-state restoration) is Phase 5.
    /// </summary>
    private void ApplySolo(ChannelViewModel changed)
    {
        var section = Outputs.Contains(changed) ? Outputs : Inputs;

        if (changed.IsSoloed)
            foreach (var ch in section)
                if (ch != changed) ch.IsSoloed = false;

        bool anySolo = section.Any(c => c.IsSoloed);
        foreach (var ch in section)
            ch.IsDimmed = anySolo && !ch.IsSoloed;
    }

    private void TickVu()
    {
        TickSection(Outputs);
        TickSection(Inputs);
    }

    private static void TickSection(ObservableCollection<ChannelViewModel> section)
    {
        bool anySolo = section.Any(c => c.IsSoloed);
        foreach (var ch in section)
        {
            bool active = !ch.IsMuted && (!anySolo || ch.IsSoloed);
            if (!active) { ch.Peak = 0; continue; }

            double baseLevel = ch.Volume / 100.0;
            double jitter = 35 + Random.Shared.NextDouble() * 55
                            + Math.Sin(Environment.TickCount64 / 300.0 + ch.Volume) * 10;
            ch.Peak = Math.Clamp(baseLevel * jitter, 0, 100);
        }
    }
}
