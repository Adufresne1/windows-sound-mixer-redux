using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SoundMixerRedux.Services;

/// <summary>
/// Entry point to Windows Core Audio (WASAPI) via NAudio.
/// Owns the active render/capture devices, exposes their Master volume/mute
/// (<see cref="Output"/>/<see cref="Input"/>) and the render device's per-app sessions,
/// grouped by process (one <see cref="AudioSessionGroup"/> per app, like the Windows Volume Mixer).
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DispatcherQueue _ui;

    private MMDevice? _outputDevice;
    private MMDevice? _inputDevice;
    private AudioSessionManager? _outputSessionManager;

    private readonly Dictionary<uint, AudioSessionGroup> _groups = new();
    private readonly NotificationClient _notificationClient;

    public AudioEndpointController Output { get; }
    public AudioEndpointController Input { get; }

    /// <summary>Raised on the UI thread when a device is added/removed or changes state.</summary>
    public event Action? DevicesChanged;

    /// <summary>Raised on the UI thread when the default device changes (argument = the affected flow).</summary>
    public event Action<DataFlow>? DefaultDeviceChanged;

    /// <summary>One entry per process (an app's sessions grouped together).</summary>
    public List<AudioSessionGroup> OutputGroups { get; } = new();

    /// <summary>Raised on the UI thread whenever the output group list changes.</summary>
    public event Action? OutputGroupsChanged;

    public AudioService(DispatcherQueue ui)
    {
        _ui = ui;
        Output = new AudioEndpointController(ui);
        Input = new AudioEndpointController(ui);
        _notificationClient = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
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

    /// <summary>Target a render device: master volume/mute + its per-app session groups.</summary>
    public void SetOutputDevice(string id)
    {
        Output.Detach();
        ClearOutputGroups();
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
        BuildOutputGroups();

        OutputGroupsChanged?.Invoke();
    }

    /// <summary>Target a capture device (master level only — per-app capture sessions are out of scope).</summary>
    public void SetInputDevice(string id)
    {
        Input.Detach();
        _inputDevice?.Dispose();
        _inputDevice = _enumerator.GetDevice(id);
        Input.Attach(_inputDevice);
    }

    /// <summary>Make the given endpoint the Windows default (render or capture — the id carries the flow).</summary>
    public void SetDefaultDevice(string id) => PolicyConfig.SetDefault(id);

    private void BuildOutputGroups()
    {
        if (_outputSessionManager == null) return;
        var sessions = _outputSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var control = sessions[i];
            if (control.State == AudioSessionState.AudioSessionStateExpired) continue;
            AddSessionToGroup(control);
        }
    }

    private void AddSessionToGroup(AudioSessionControl control)
    {
        var member = new AudioSessionController(control, _ui);
        uint key = member.IsSystemSounds ? 0u : member.ProcessId;

        if (!_groups.TryGetValue(key, out var group))
        {
            group = new AudioSessionGroup(member.ProcessId, member.IsSystemSounds);
            group.Emptied += () => RemoveGroup(key);
            _groups[key] = group;
            OutputGroups.Add(group);
        }
        group.Add(member);
    }

    private void RemoveGroup(uint key)
    {
        if (!_groups.TryGetValue(key, out var group)) return;
        _groups.Remove(key);
        OutputGroups.Remove(group);
        group.Dispose();
        OutputGroupsChanged?.Invoke();
    }

    private void OnSessionCreated(object? sender, IAudioSessionControl newSession)
    {
        // Callback arrives on an MTA COM thread — marshal to the UI thread.
        _ui.TryEnqueue(() =>
        {
            AddSessionToGroup(new AudioSessionControl(newSession));
            OutputGroupsChanged?.Invoke();
        });
    }

    private void ClearOutputGroups()
    {
        foreach (var group in OutputGroups)
            group.Dispose();
        OutputGroups.Clear();
        _groups.Clear();
    }

    private void RaiseDevicesChanged() => _ui.TryEnqueue(() => DevicesChanged?.Invoke());
    private void RaiseDefaultChanged(DataFlow flow) => _ui.TryEnqueue(() => DefaultDeviceChanged?.Invoke(flow));

    /// <summary>Bridges Core Audio endpoint notifications (MTA) to the service's UI-thread events.</summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly AudioService _service;
        public NotificationClient(AudioService service) => _service = service;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _service.RaiseDevicesChanged();
        public void OnDeviceAdded(string pwstrDeviceId) => _service.RaiseDevicesChanged();
        public void OnDeviceRemoved(string deviceId) => _service.RaiseDevicesChanged();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Console/Multimedia define the "default device"; skip Communications to avoid duplicate churn.
            if (role != Role.Communications)
                _service.RaiseDefaultChanged(flow);
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
        ClearOutputGroups();
        if (_outputSessionManager != null)
            _outputSessionManager.OnSessionCreated -= OnSessionCreated;
        Output.Dispose();
        Input.Dispose();
        _outputDevice?.Dispose();
        _inputDevice?.Dispose();
        _enumerator.Dispose();
    }
}
