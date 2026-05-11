using System.Drawing;

namespace SioForgeCAD.Commun.Extensions;

public static class ColorsEntensions
{
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
        var DrawingColor = BaseColor.ColorValue;
        var Gray = (byte)(0.2989 * DrawingColor.R + 0.5870 * DrawingColor.G + 0.1140 * DrawingColor.B);
        return Autodesk.AutoCAD.Colors.Color.FromRgb(Gray, Gray, Gray);
    }
}
