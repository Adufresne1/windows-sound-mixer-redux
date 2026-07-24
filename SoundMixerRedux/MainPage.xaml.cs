using Microsoft.UI.Xaml.Controls;
using SoundMixerRedux.ViewModels;

namespace SoundMixerRedux;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>The mixer view model (instantiated as this page's DataContext in XAML).</summary>
    public MixerViewModel ViewModel => (MixerViewModel)DataContext;
}
