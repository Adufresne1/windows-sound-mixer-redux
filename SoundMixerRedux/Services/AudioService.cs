using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;

namespace SoundMixerRedux.Services;

/// <summary>
/// Entry point to Windows Core Audio (WASAPI) via NAudio. Phase 2: device enumeration
/// and master volume/mute of the default render/capture endpoints. Sessions (per-app),
/// meters and default-device switching arrive in later phases.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public AudioEndpointController Output { get; }
    public AudioEndpointController Input { get; }

    public AudioService(DispatcherQueue ui)
    {
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

    /// <summary>Resolve a device by id. Caller/consumer owns the returned instance (handed to a controller).</summary>
    public MMDevice GetDevice(string id) => _enumerator.GetDevice(id);

    public void Dispose()
    {
        Output.Dispose();
        Input.Dispose();
        _enumerator.Dispose();
    }
}
