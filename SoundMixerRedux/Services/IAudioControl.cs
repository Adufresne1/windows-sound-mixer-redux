namespace SoundMixerRedux.Services;

/// <summary>Common volume/mute surface shared by endpoint (Master) and per-app session controllers.</summary>
public interface IAudioControl
{
    /// <summary>Volume as a 0..1 scalar.</summary>
    float VolumeScalar { get; set; }

    bool Mute { get; set; }

    /// <summary>Current peak level as a 0..1 linear scalar (for VU metering).</summary>
    float Peak { get; }
}
