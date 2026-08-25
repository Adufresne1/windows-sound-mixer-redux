using System;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SoundMixerRedux.Converters;

/// <summary>Maps a 0..100 percentage to a pixel height. ConverterParameter = max height (default 180).</summary>
public sealed class PercentToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double pct = value is double d ? d : 0;
        double max = 180;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
            max = m;
        return Math.Clamp(pct, 0, 100) / 100.0 * max;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Maps a 0..100 percentage to the *remaining* pixel height (max − value). Used to mask the
/// unfilled top of a VU meter so the colour zones stay anchored to the meter's full height.</summary>
public sealed class PercentToRemainingHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double pct = value is double d ? d : 0;
        double max = 180;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
            max = m;
        return (1.0 - Math.Clamp(pct, 0, 100) / 100.0) * max;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Parses a "#RRGGBB" / "#AARRGGBB" hex string into a SolidColorBrush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => new SolidColorBrush(ParseHex(value as string));

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static Color ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Gray;
        hex = hex.TrimStart('#');

        byte a = 0xFF, r, g, b;
        if (hex.Length == 8)
        {
            a = System.Convert.ToByte(hex.Substring(0, 2), 16);
            hex = hex.Substring(2);
        }
        if (hex.Length != 6) return Colors.Gray;

        r = System.Convert.ToByte(hex.Substring(0, 2), 16);
        g = System.Convert.ToByte(hex.Substring(2, 2), 16);
        b = System.Convert.ToByte(hex.Substring(4, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }
}

/// <summary>Bool to Visibility. ConverterParameter = "invert" flips the result.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool v && v;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Multiplies ConverterParameter (a base pixel value) by the bound Scale. Used to keep every
/// ChannelStrip dimension a real layout value (not a RenderTransform), so scaling never mispositions
/// Popup-based UI (tooltips, flyouts) the way a Viewbox around this content did (see Phase 4 history).</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double scale = value is double d ? d : 1.0;
        double baseValue = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var b) ? b : 0;
        double scaled = baseValue * scale;
        return targetType == typeof(Thickness) ? new Thickness(scaled) : scaled;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
