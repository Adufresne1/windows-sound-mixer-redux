using Microsoft.UI.Xaml.Controls;

namespace SoundMixerRedux.Controls;

/// <summary>
/// One vertical mixer channel (icon, name, %, VU meter, fader, Mute/Solo).
/// DataContext is expected to be a <see cref="ViewModels.ChannelViewModel"/>.
/// </summary>
public sealed partial class ChannelStrip : UserControl
{
    public ChannelStrip()
    {
        InitializeComponent();
    }
}
