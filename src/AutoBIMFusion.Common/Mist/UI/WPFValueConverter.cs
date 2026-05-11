using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SioForgeCAD.Commun.Mist.UI;

public class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var total = (double)value;
        var percent = System.Convert.ToDouble(parameter);
        return new GridLength(total * percent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class WidthToLeftMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = (double)value;
        return new Thickness(width, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LighteningConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color baseColor)
        {
            var factor = 0.2f;
            var parameterText = parameter?.ToString();
            if (!string.IsNullOrWhiteSpace(parameterText))
                float.TryParse(parameterText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out factor);

            return Color.FromRgb(
                CalculateChannel(baseColor.R, factor),
                CalculateChannel(baseColor.G, factor),
                CalculateChannel(baseColor.B, factor)
            );
        }

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static byte CalculateChannel(byte channel, float factor)
    {
        float result;
        if (factor >= 0)
            // Éclaircir : on réduit la distance vers 255
            result = channel + (255 - channel) * factor;
        else
            // Assombrir : on réduit la valeur vers 0
            result = channel * (1 + factor);

        return (byte)Max(0, Min(255, result));
    }
}
