using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SoundMixerRedux.Services;

/// <summary>
/// Entry point to Windows Core Audio (WASAPI) via NAudio.
/// Owns the active render/capture devices, exposes their Master volume/mute
/// (<see cref="Output"/>/<see cref="Input"/>) and the render device's per-app sessions.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DispatcherQueue _ui;

    private MMDevice? _outputDevice;
    private MMDevice? _inputDevice;
    private AudioSessionManager? _outputSessionManager;

    public AudioEndpointController Output { get; }
    public AudioEndpointController Input { get; }

    /// <summary>Live per-app sessions of the current output device.</summary>
    public List<AudioSessionController> OutputSessions { get; } = new();

    /// <summary>Raised on the UI thread whenever the output session list changes.</summary>
    public event Action? OutputSessionsChanged;

    public AudioService(DispatcherQueue ui)
    {
        _ui = ui;
        Output = new AudioEndpointController(ui);
        Input = new AudioEndpointController(ui);
    }

    public List<AudioDeviceInfo> GetDevices(DataFlow flow)
    {
        var list = new List<AudioDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
                list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
        }
        return list;
    }

    public string? GetDefaultDeviceId(DataFlow flow)
    {
        if (!_enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            return null;
        using var device = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        return device.ID;
    }

    /// <summary>Target a render device: master volume/mute + its per-app sessions.</summary>
    public void SetOutputDevice(string id)
    {
        Output.Detach();
        ClearOutputSessions();
        if (_outputSessionManager != null)
        {
            _outputSessionManager.OnSessionCreated -= OnSessionCreated;
            _outputSessionManager = null;
        }
        _outputDevice?.Dispose();

        _outputDevice = _enumerator.GetDevice(id);
        Output.Attach(_outputDevice);

        _outputSessionManager = _outputDevice.AudioSessionManager;
        _outputSessionManager.OnSessionCreated += OnSessionCreated;
        BuildOutputSessions();

        OutputSessionsChanged?.Invoke();
    }

    /// <summary>Target a capture device (master level only — per-app capture sessions are out of scope).</summary>
    public void SetInputDevice(string id)
    {
        Input.Detach();
        _inputDevice?.Dispose();
        _inputDevice = _enumerator.GetDevice(id);
        Input.Attach(_inputDevice);
    }

    private void BuildOutputSessions()
    {
        if (_outputSessionManager == null) return;
        var sessions = _outputSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var control = sessions[i];
            if (control.State == AudioSessionState.AudioSessionStateExpired) continue;
            AddSession(control);
        }
    }

    private void AddSession(AudioSessionControl control)
    {
        var controller = new AudioSessionController(control, _ui);
        controller.Disconnected += () => RemoveSession(controller);
        OutputSessions.Add(controller);
    }

    private void RemoveSession(AudioSessionController controller)
    {
        if (!OutputSessions.Remove(controller))
            return;
        controller.Dispose();
        OutputSessionsChanged?.Invoke();
    }

    private void OnSessionCreated(object? sender, IAudioSessionControl newSession)
    {
        // Callback arrives on an MTA COM thread — marshal to the UI thread.
        _ui.TryEnqueue(() =>
        {
            AddSession(new AudioSessionControl(newSession));
            OutputSessionsChanged?.Invoke();
        });
    }

    private void ClearOutputSessions()
    {
        foreach (var session in OutputSessions)
            session.Dispose();
        OutputSessions.Clear();
    }

    public void Dispose()
    {
        ClearOutputSessions();
        if (_outputSessionManager != null)
            _outputSessionManager.OnSessionCreated -= OnSessionCreated;
        Output.Dispose();
        Input.Dispose();
        _outputDevice?.Dispose();
        _inputDevice?.Dispose();
        _enumerator.Dispose();
    }
}
