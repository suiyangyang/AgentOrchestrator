using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AgentOrchestrator.App.Converters;

public class BoolToPasswordCharConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible && isVisible)
            return '\0';
        return '•';
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is char c)
            return c == '\0';
        return false;
    }
}
