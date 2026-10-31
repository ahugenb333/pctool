using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PcTool.Compatibility;
using PcTool.Core.Models;

namespace PcTool.App.Converters;

internal static class Palette
{
    public static readonly SolidColorBrush Good = Freeze(Color.FromRgb(0x4C, 0xC2, 0x6A));
    public static readonly SolidColorBrush Ok = Freeze(Color.FromRgb(0x60, 0xA5, 0xFA));
    public static readonly SolidColorBrush Warn = Freeze(Color.FromRgb(0xF5, 0xB0, 0x41));
    public static readonly SolidColorBrush Bad = Freeze(Color.FromRgb(0xEF, 0x5B, 0x5B));
    public static readonly SolidColorBrush Neutral = Freeze(Color.FromRgb(0x9C, 0xA3, 0xAF));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        FindingSeverity.Problem => Palette.Bad,
        FindingSeverity.Warning => Palette.Warn,
        _ => Palette.Ok
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class VerdictToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        GameVerdictLevel.RecommendedPlus => Palette.Good,
        GameVerdictLevel.Recommended => Palette.Ok,
        GameVerdictLevel.Minimum => Palette.Warn,
        _ => Palette.Bad
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CheckStatus.Pass => Palette.Good,
        CheckStatus.Warn => Palette.Warn,
        CheckStatus.Fail => Palette.Bad,
        _ => Palette.Neutral
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
