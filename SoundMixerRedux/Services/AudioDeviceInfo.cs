namespace SoundMixerRedux.Services;

/// <summary>Lightweight, UI-friendly view of a Core Audio endpoint device.</summary>
public sealed class AudioDeviceInfo
{
    public AudioDeviceInfo(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; set; }
    public string Name { get; set; }

    // ComboBox etc. display this.
    public override string ToString() => Name;
}
