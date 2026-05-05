using Autodesk.AutoCAD.Colors;
using Serilog.Core;
using System.Globalization;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Диагностические утилиты для логирования состояния размерных и текстовых стилей базы данных.
/// Используется для отладки: снимок до/после слияния позволяет отследить
/// аномалии масштабирования и коллизии стилей.
/// </summary>
internal static class DimensionStyleDiagnosticUtils
{
    private static readonly HashSet<string> StandardStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISO-25",
        "Standard",
        "Annotative"
    };

    /// <summary>
    /// Записывает в лог снимок всех пользовательских размерных и текстовых стилей базы данных.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    /// <param name="log">Экземпляр логгера.</param>
    /// <param name="stage">Метка этапа (например: "after-merge").</param>
    internal static void LogStyleSnapshot(Database db, Logger log, string stage)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)trx.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        TextStyleTable textStyleTable = (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead);

        List<string> dimStyles = [];
        foreach (ObjectId id in dimStyleTable)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style))
            {
                dimStyles.Add(FormatDimensionStyle(style));
            }
        }

        List<string> textStyles = [];
        foreach (ObjectId id in textStyleTable)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style))
            {
                textStyles.Add(FormatTextStyle(style));
            }
        }

        trx.Commit();

        log.Information($"[STYLE-SNAPSHOT] stage={stage}, dimStyles={dimStyles.Count}, textStyles={textStyles.Count}");

        foreach (string style in dimStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[DIM-STYLE] stage={stage}, {style}");
        }

        foreach (string style in textStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[TEXT-STYLE] stage={stage}, {style}");
        }
    }

    private static bool IsUserStyle(SymbolTableRecord style)
    {
        return !style.IsDependent && !style.IsErased && !StandardStyleNames.Contains(style.Name);
    }

    private static string FormatDimensionStyle(DimStyleTableRecord style)
    {
        return
            $"styleName=\"{Escape(style.Name)}\", styleHandle={style.Handle}, " +
            $"Dimtxt={F(style.Dimtxt)}, Dimasz={F(style.Dimasz)}, " +
            $"Dimscale={F(style.Dimscale)}, Dimlfac={F(style.Dimlfac)}, " +
            $"Dimexo={F(style.Dimexo)}, Dimexe={F(style.Dimexe)}, " +
            $"Dimtad={style.Dimtad}, Dimjust={style.Dimjust}, " +
            $"Dimgap={F(style.Dimgap)}, Dimdec={style.Dimdec}, " +
            $"Dimrnd={F(style.Dimrnd)}, Dimpost=\"{Escape(style.Dimpost)}\", " +
            $"Dimclrd={FormatColor(style.Dimclrd)}, Dimclre={FormatColor(style.Dimclre)}, " +
            $"Dimclrt={FormatColor(style.Dimclrt)}, Annotative={style.Annotative}, " +
            $"VisualText={F(style.Dimtxt * style.Dimscale)}, VisualArrow={F(style.Dimasz * style.Dimscale)}";
    }

    private static string FormatTextStyle(TextStyleTableRecord style)
    {
        Autodesk.AutoCAD.GraphicsInterface.FontDescriptor font = style.Font;
        return
            $"styleName=\"{Escape(style.Name)}\", styleHandle={style.Handle}, " +
            $"styleFile=\"{Escape(style.FileName)}\", styleBigFont=\"{Escape(style.BigFontFileName)}\", " +
            $"styleTypeface=\"{Escape(font.TypeFace)}\", styleBold={font.Bold}, styleItalic={font.Italic}, " +
            $"styleCharacterSet={font.CharacterSet}, stylePitchAndFamily={font.PitchAndFamily}, " +
            $"styleIsShapeFile={style.IsShapeFile}, styleIsVertical={style.IsVertical}, " +
            $"styleTextSize={F(style.TextSize)}, styleXScale={F(style.XScale)}, " +
            $"styleObliquingAngle={F(style.ObliquingAngle)}";
    }

    private static string FormatColor(Color color)
    {
        return $"{color.ColorMethod}:{color.ColorIndex}";
    }

    private static string F(double value)
    {
        return double.IsFinite(value) ? value.ToString("F6", CultureInfo.InvariantCulture) : "n/a";
    }

    private static string Escape(string? value)
    {
        return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
