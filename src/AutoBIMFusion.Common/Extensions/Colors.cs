using AutoBIMFusion.Common.Compatibility;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Mist.AutoCAD;
using Autodesk.AutoCAD.Colors;
using Color = System.Drawing.Color;

namespace AutoBIMFusion.Common.Extensions;

public static class ColorsEntensions
{
    // --- Original Extensions/Colors.cs methods ---

    public static Color GetSystemDrawingColor(this Entity ent)
    {
        return ent.Color.ColorValue;
    }

    public static Autodesk.AutoCAD.Colors.Color GetColor(this Entity ent)
    {
        return ent.Color;
    }

    public static Autodesk.AutoCAD.Colors.Color ConvertColorToGray(this Autodesk.AutoCAD.Colors.Color BaseColor)
    {
        Color DrawingColor = BaseColor.ColorValue;
        byte Gray = (byte)((0.2989 * DrawingColor.R) + (0.5870 * DrawingColor.G) + (0.1140 * DrawingColor.B));
        return Autodesk.AutoCAD.Colors.Color.FromRgb(Gray, Gray, Gray);
    }

    // --- Merged from Mist/Helpers/Colors.cs ---

    public static Autodesk.AutoCAD.Colors.Color GetRealColor(this Entity ent)
    {
        Autodesk.AutoCAD.Colors.Color DefinedColor;
        if (ent.Color.IsByLayer)
        {
            string EntityLayer = ent.Layer;
            ObjectId LayerTableRecordObjId = Layers.GetLayerIdByName(EntityLayer);
            DefinedColor = Layers.GetLayerColor(LayerTableRecordObjId);
        }
        else
        {
            DefinedColor = ent.Color;
        }

        return Autodesk.AutoCAD.Colors.Color.FromRgb(DefinedColor.ColorValue.R, DefinedColor.ColorValue.G,
            DefinedColor.ColorValue.B);
    }

    public static string ColorToHex(this Autodesk.AutoCAD.Colors.Color acadColor)
    {
        if (acadColor == null)
        {
            return "#000000"; // Or string.Empty
        }

        Color rgb = acadColor.ColorValue;
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    public static (double R, double G, double B) SetBrightness(double BrightnessFactor, double R, double G, double B)
    {
        //BrightnessFactor need to be between -1 and 1
        double SetBrignessChannel(double Channel)
        {
            double ScaledValue = Channel * (1 + BrightnessFactor);
            return ScaledValue.Clamp(0, 255);
        }

        return (SetBrignessChannel(R), SetBrignessChannel(G), SetBrignessChannel(B));
    }

    public static (double R, double G, double B) SetContrast(double ContrastFactor, double R, double G, double B)
    {
        //BrightnessFactor need to be between -1 and 1
        double ContrastLevel = Pow((1.0 + ContrastFactor) / 1.0, 2);

        double SetContrastChannel(double Channel)
        {
            double ScaledValue = ((((Channel / 255.0) - 0.5) * ContrastLevel) + 0.5) * 255.0;
            return ScaledValue.Clamp(0, 255);
        }

        return (SetContrastChannel(R), SetContrastChannel(G), SetContrastChannel(B));
    }

    public static Autodesk.AutoCAD.Colors.Color GetTransGraphicsColor(Entity _, bool IsPrimary)
    {
        int PrimaryColorIndex = Settings.TransientPrimaryColorIndex;
        int SecondaryColorIndex = Settings.TransientSecondaryColorIndex;

        return Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByColor,
            !IsPrimary ? (short)SecondaryColorIndex : (short)PrimaryColorIndex);
    }

    public static (double hue, double saturation, double value) ColorToHSV(Autodesk.AutoCAD.Colors.Color color)
    {
        double hue;
        double saturation;
        double value;

        int max = Max(color.Red, Max(color.Green, color.Blue));
        int min = Min(color.Red, Min(color.Green, color.Blue));

        // Calcul de la Saturation et de la Valeur (Luminosité)
        saturation = max == 0 ? 0 : 1d - (1d * min / max);
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
            hue = max == color.Red
                ? 60d * ((color.Green - color.Blue) / delta)
                : max == color.Green
                    ? 60d * (2d + ((color.Blue - color.Red) / delta))
                    : 60d * (4d + ((color.Red - color.Green) / delta));

            // Si l'angle est négatif, on le ramène dans le cercle [0, 360[
            if (hue < 0d)
            {
                hue += 360d;
            }
        }

        return (hue, saturation, value);
    }

    public static Autodesk.AutoCAD.Colors.Color FromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Floor(hue / 60)) % 6;
        double f = (hue / 60) - Floor(hue / 60);

        value *= 255;
        byte v = (byte)value;
        byte p = (byte)(value * (1 - saturation));
        byte q = (byte)(value * (1 - (f * saturation)));
        byte t = (byte)(value * (1 - ((1 - f) * saturation)));

        return hi switch
        {
            0 => Autodesk.AutoCAD.Colors.Color.FromRgb(v, t, p),
            1 => Autodesk.AutoCAD.Colors.Color.FromRgb(q, v, p),
            2 => Autodesk.AutoCAD.Colors.Color.FromRgb(p, v, t),
            3 => Autodesk.AutoCAD.Colors.Color.FromRgb(p, q, v),
            4 => Autodesk.AutoCAD.Colors.Color.FromRgb(t, p, v),
            _ => Autodesk.AutoCAD.Colors.Color.FromRgb(v, p, q)
        };
    }
}
