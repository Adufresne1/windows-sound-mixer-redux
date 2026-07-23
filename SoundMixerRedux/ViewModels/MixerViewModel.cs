using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace SoundMixerRedux.ViewModels;

/// <summary>
/// Root view model for the mixer. Phase 1: mock channels + ambient VU animation.
/// The real audio backend (WASAPI/Core Audio) arrives in Phase 2+.
/// </summary>
public partial class MixerViewModel : ObservableObject
{
    private readonly DispatcherTimer _vuTimer;

    public ObservableCollection<ChannelViewModel> Outputs { get; } = new();
    public ObservableCollection<ChannelViewModel> Inputs { get; } = new();

    public List<string> OutputDevices { get; } = new()
    {
        "Haut-parleurs (Realtek)", "Casque (USB · Arctis)", "Sortie HDMI (Écran 2)", "Barre de son (Bluetooth)"
    };

    public List<string> InputDevices { get; } = new()
    {
        "Microphone (Realtek)", "Casque-micro (USB · Arctis)", "Webcam C920", "Mix stéréo"
    };

    [ObservableProperty] private string _selectedOutputDevice;
    [ObservableProperty] private string _selectedInputDevice;

    // Settings (mock, visual only in Phase 1 — persisted for real in Phase 7).
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _showDbScale = true;

    public MixerViewModel()
    {
        _selectedOutputDevice = OutputDevices[0];
        _selectedInputDevice = InputDevices[0];

        AddOutput(new ChannelViewModel { Name = "Système", IsMaster = true, Volume = 80, Glyph = "", TileColor = "#3A7BD5" });
        AddOutput(new ChannelViewModel { Name = "Spotify", Volume = 65, Initials = "Sp", TileColor = "#1DB954" });
        AddOutput(new ChannelViewModel { Name = "Chrome", Volume = 100, Initials = "Ch", TileColor = "#4285F4" });
        AddOutput(new ChannelViewModel { Name = "Discord", Volume = 72, Initials = "Dc", TileColor = "#5865F2" });
        AddOutput(new ChannelViewModel { Name = "Forza", Volume = 55, Initials = "Fz", TileColor = "#7B2FF7" });

        AddInput(new ChannelViewModel { Name = "Microphone", IsMaster = true, Volume = 70, Glyph = "", TileColor = "#1F9D6B" });
        AddInput(new ChannelViewModel { Name = "Discord", Volume = 85, Initials = "Dc", TileColor = "#5865F2" });

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

    /// <summary>
    /// Phase 1 solo preview: exclusive within a section (radio) + dims the others.
    /// The full solo logic (Windows mute + prior-state restoration) is Phase 5.
    /// </summary>
    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChannelViewModel.IsSoloed) || sender is not ChannelViewModel changed)
            return;

        var section = Outputs.Contains(changed) ? Outputs : Inputs;

        if (changed.IsSoloed)
        {
            foreach (var ch in section)
                if (ch != changed) ch.IsSoloed = false;
        }

        bool anySolo = false;
        foreach (var ch in section)
            if (ch.IsSoloed) { anySolo = true; break; }

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
        bool anySolo = false;
        foreach (var ch in section)
            if (ch.IsSoloed) { anySolo = true; break; }

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
