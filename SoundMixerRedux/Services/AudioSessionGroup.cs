using System;
using System.Collections.Generic;

namespace SoundMixerRedux.Services;

/// <summary>
/// One mixer channel's worth of audio: all sessions belonging to a single process, controlled together.
/// The Windows Volume Mixer groups an app's sessions the same way (one slider per app, not per stream).
/// </summary>
public sealed class AudioSessionGroup : IAudioControl, IDisposable
{
    private readonly List<AudioSessionController> _members = new();

    public AudioSessionGroup(uint processId, bool isSystemSounds)
    {
        ProcessId = processId;
        IsSystemSounds = isSystemSounds;
    }

    public uint ProcessId { get; }
    public bool IsSystemSounds { get; }
    public int Count => _members.Count;

    /// <summary>Raised (UI thread) when any member reports an external volume/mute change.</summary>
    public event Action? ExternalChange;

    /// <summary>Raised (UI thread) when the last member has disconnected.</summary>
    public event Action? Emptied;

    public void Add(AudioSessionController member)
    {
        _members.Add(member);
        member.ExternalChange += OnMemberExternalChange;
        member.Disconnected += () => OnMemberDisconnected(member);
    }

    private void OnMemberExternalChange() => ExternalChange?.Invoke();

    private void OnMemberDisconnected(AudioSessionController member)
    {
        if (!_members.Remove(member)) return;
        member.ExternalChange -= OnMemberExternalChange;
        member.Dispose();
        if (_members.Count == 0)
            Emptied?.Invoke();
        else
            ExternalChange?.Invoke();
    }

    public float VolumeScalar
    {
        get => _members.Count > 0 ? _members[0].VolumeScalar : 0f;
        set { foreach (var m in _members) m.VolumeScalar = value; }
    }

    public bool Mute
    {
        get => _members.Count > 0 && _members[0].Mute;
        set { foreach (var m in _members) m.Mute = value; }
    }

    public float Peak
    {
        get
        {
            float peak = 0f;
            foreach (var m in _members)
                if (m.Peak > peak) peak = m.Peak;
            return peak;
        }
    }

    public void Dispose()
    {
        foreach (var m in _members) m.Dispose();
        _members.Clear();
        ExternalChange = null;
        Emptied = null;
    }
}
