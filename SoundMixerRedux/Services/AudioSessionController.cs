using System;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SoundMixerRedux.Services;

/// <summary>
/// Wraps a single per-app audio session (ISimpleAudioVolume). Volume/mute both ways,
/// plus external-change and disconnect notifications marshalled to the UI thread.
/// </summary>
public sealed class AudioSessionController : IAudioControl, IAudioSessionEventsHandler, IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly AudioSessionControl _session;

    public AudioSessionController(AudioSessionControl session, DispatcherQueue ui)
    {
        _session = session;
        _ui = ui;
        _session.RegisterEventClient(this);
    }

    /// <summary>Raised on the UI thread when Windows reports a volume/mute change for this session.</summary>
    public event Action? ExternalChange;

    /// <summary>Raised on the UI thread when the session expires or is disconnected.</summary>
    public event Action? Disconnected;

    public uint ProcessId
    {
        get { try { return _session.GetProcessID; } catch { return 0; } }
    }

    public bool IsSystemSounds
    {
        get { try { return _session.IsSystemSoundsSession; } catch { return false; } }
    }

    public float VolumeScalar
    {
        get => _session.SimpleAudioVolume.Volume;
        set => _session.SimpleAudioVolume.Volume = Math.Clamp(value, 0f, 1f);
    }

    public bool Mute
    {
        get => _session.SimpleAudioVolume.Mute;
        set => _session.SimpleAudioVolume.Mute = value;
    }

    public float Peak
    {
        get { try { return _session.AudioMeterInformation.MasterPeakValue; } catch { return 0f; } }
    }

    // ---- IAudioSessionEventsHandler (callbacks arrive on MTA COM threads) ----

    public void OnVolumeChanged(float volume, bool isMuted) => _ui.TryEnqueue(() => ExternalChange?.Invoke());

    public void OnStateChanged(AudioSessionState state)
    {
        if (state == AudioSessionState.AudioSessionStateExpired)
            _ui.TryEnqueue(() => Disconnected?.Invoke());
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        => _ui.TryEnqueue(() => Disconnected?.Invoke());

    public void OnDisplayNameChanged(string displayName) { }
    public void OnIconPathChanged(string iconPath) { }
    public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
    public void OnGroupingParamChanged(ref Guid groupingId) { }

    public void Dispose()
    {
        // NAudio owns/disposes the underlying AudioSessionControl via its SessionManager.
        ExternalChange = null;
        Disconnected = null;
    }
}
