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
        CheckStatus.Pass => Brush("#2E7D32"),
        CheckStatus.Warn => Brush("#B26A00"),
        CheckStatus.Block => Brush("#C62828"),
        HealthVerdict.Streamable => Brush("#2E7D32"),
        HealthVerdict.AtRisk => Brush("#B26A00"),
        HealthVerdict.Blocked => Brush("#C62828"),
        _ => Brush("#6A737C"),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
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