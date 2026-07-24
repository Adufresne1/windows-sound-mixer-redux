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

    public AudioEndpointController Output { get; }
    public AudioEndpointController Input { get; }

    /// <summary>One entry per process (an app's sessions grouped together).</summary>
    public List<AudioSessionGroup> OutputGroups { get; } = new();

    /// <summary>Raised on the UI thread whenever the output group list changes.</summary>
    public event Action? OutputGroupsChanged;

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

    public void Dispose()
    {
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
