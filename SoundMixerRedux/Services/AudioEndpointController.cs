using System;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;

namespace SoundMixerRedux.Services;

/// <summary>
/// Wraps a single Core Audio endpoint (render or capture) and exposes its master
/// volume/mute. External changes (keyboard, Windows panel, other apps) are surfaced
/// via <see cref="ExternalChange"/>, always raised on the UI thread.
/// </summary>
public sealed class AudioEndpointController : IDisposable
{
    private readonly DispatcherQueue _ui;
    private MMDevice? _device;

    public AudioEndpointController(DispatcherQueue ui) => _ui = ui;

    /// <summary>Raised on the UI thread when Windows reports a volume/mute change on this endpoint.</summary>
    public event Action? ExternalChange;

    public bool HasDevice => _device != null;

    /// <summary>Master volume as a 0..1 scalar (matches the Windows slider taper).</summary>
    public float VolumeScalar
    {
        get => _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f;
        set { if (_device != null) _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }
    }

    public bool Mute
    {
        get => _device?.AudioEndpointVolume.Mute ?? false;
        set { if (_device != null) _device.AudioEndpointVolume.Mute = value; }
    }

    /// <summary>Point this controller at a new device, releasing any previous one.</summary>
    public void Attach(MMDevice device)
    {
        Detach();
        _device = device;
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
    }

    public void Detach()
    {
        if (_device == null) return;
        try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; }
        catch { /* device may already be gone */ }
        try { _device.Dispose(); } catch { }
        _device = null;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        // Callback arrives on an MTA COM thread — marshal to the UI thread before touching VMs.
        _ui.TryEnqueue(() => ExternalChange?.Invoke());
    }

    public void Dispose() => Detach();
}
