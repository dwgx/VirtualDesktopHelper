using System.Windows;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VdHelper.Core.Model;

namespace VdHelper;

/// <summary>Status colour mapping shared by the health list and the verdict banner.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CheckStatus.Pass => Resource("PassBrush"),
        CheckStatus.Warn => Resource("WarnBrush"),
        CheckStatus.Block => Resource("BlockBrush"),
        HealthVerdict.Streamable => Resource("PassBrush"),
        HealthVerdict.AtRisk => Resource("WarnBrush"),
        HealthVerdict.Blocked => Resource("BlockBrush"),
        _ => Resource("UnknownBrush"),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    /// <summary>
    /// One palette, in App.xaml. This converter used to carry its own hardcoded hexes, and those
    /// were measurably worse than the ones they duplicated: on the panel background (#101010) they
    /// measured 3.38:1 to 4.49:1, all four below WCAG AA's 4.5:1 for body text, while PassBrush /
    /// WarnBrush / BlockBrush sat unused in App.xaml at 8.93 / 8.37 / 5.27. Two palettes, the
    /// compliant one dead. Ratios recomputed against #101010 after the change: 8.93 / 8.37 / 5.27 /
    /// 5.86 — every state passes AA with room to spare.
    /// </summary>
    private static Brush Resource(string key) =>
        Application.Current?.TryFindResource(key) as Brush
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8F98"));


}

public sealed class StatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CheckStatus.Pass => "通过",
        CheckStatus.Warn => "警告",
        CheckStatus.Block => "阻断",
        HealthVerdict.Streamable => "可串流",
        HealthVerdict.AtRisk => "有隐患",
        HealthVerdict.Blocked => "阻断",
        _ => "未知",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}