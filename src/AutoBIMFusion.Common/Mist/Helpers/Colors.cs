using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist.AutoCAD;
using Autodesk.AutoCAD.Colors;

namespace AutoBIMFusion.Common.Mist.Helpers;

public static class Colors
{
    public static Color GetRealColor(this Entity ent)
    {
        Color DefinedColor;
        if (ent.Color.IsByLayer)
        {
            var EntityLayer = ent.Layer;
            var LayerTableRecordObjId = Layers.GetLayerIdByName(EntityLayer);
            DefinedColor = Layers.GetLayerColor(LayerTableRecordObjId);
        }
        else
        {
            DefinedColor = ent.Color;
        }

        return Color.FromRgb(DefinedColor.ColorValue.R, DefinedColor.ColorValue.G, DefinedColor.ColorValue.B);
    }


    public static string ColorToHex(this Color acadColor)
    {
        if (acadColor == null) return "#000000"; // Or string.Empty

        var rgb = acadColor.ColorValue;
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    public static (double R, double G, double B) SetBrightness(double BrightnessFactor, double R, double G, double B)
    {
        //BrightnessFactor need to be between -1 and 1
        double SetBrignessChannel(double Channel)
        {
            var ScaledValue = Channel * (1 + BrightnessFactor);
            return ScaledValue.Clamp(0, 255);
        }

        return (SetBrignessChannel(R), SetBrignessChannel(G), SetBrignessChannel(B));
    }

    public static (double R, double G, double B) SetContrast(double ContrastFactor, double R, double G, double B)
    {
        //BrightnessFactor need to be between -1 and 1
        var ContrastLevel = Pow((1.0 + ContrastFactor) / 1.0, 2);

        double SetContrastChannel(double Channel)
        {
            var ScaledValue = ((Channel / 255.0 - 0.5) * ContrastLevel + 0.5) * 255.0;
            return ScaledValue.Clamp(0, 255);
        }

        return (SetContrastChannel(R), SetContrastChannel(G), SetContrastChannel(B));
    }


    public static Color GetTransGraphicsColor(Entity _, bool IsPrimary)
    {
        return Color.FromColorIndex(ColorMethod.ByColor,
            !IsPrimary ? (short)Settings.TransientSecondaryColorIndex : (short)Settings.TransientPrimaryColorIndex);
    }

    public static (double hue, double saturation, double value) ColorToHSV(Color color)
    {
        double hue;
        double saturation;
        double value;

        int max = Max(color.Red, Max(color.Green, color.Blue));
        int min = Min(color.Red, Min(color.Green, color.Blue));

        // Calcul de la Saturation et de la Valeur (Luminosité)
        saturation = max == 0 ? 0 : 1d - 1d * min / max;
        value = max / 255d;

        // Calcul de la Teinte (Hue)
        if (max == min)
        {
            // Nuance de gris : pas de teinte spécifique, on met 0 par défaut
            hue = 0d;
        }
        else
        {
            double delta = max - min;
            if (max == color.Red)
                hue = 60d * ((color.Green - color.Blue) / delta);
            else if (max == color.Green)
                hue = 60d * (2d + (color.Blue - color.Red) / delta);
            else
                hue = 60d * (4d + (color.Red - color.Green) / delta);

            // Si l'angle est négatif, on le ramène dans le cercle [0, 360[
            if (hue < 0d) hue += 360d;
        }

        return (hue, saturation, value);
    }

    public static Color FromHSV(double hue, double saturation, double value)
    {
        var hi = Convert.ToInt32(Floor(hue / 60)) % 6;
        var f = hue / 60 - Floor(hue / 60);

        value *= 255;
        var v = (byte)value;
        var p = (byte)(value * (1 - saturation));
        var q = (byte)(value * (1 - f * saturation));
        var t = (byte)(value * (1 - (1 - f) * saturation));

        switch (hi)
        {
            case 0:
                return Color.FromRgb(v, t, p);
            case 1:
                return Color.FromRgb(q, v, p);
            case 2:
                return Color.FromRgb(p, v, t);
            case 3:
                return Color.FromRgb(p, q, v);
            case 4:
                return Color.FromRgb(t, p, v);
            default:
                return Color.FromRgb(v, p, q);
        }
    }
}
