using Autodesk.AutoCAD.Colors;
using Serilog.Core;
using System.Globalization;
using System.Reflection;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Диагностические утилиты для логирования состояния размерных и текстовых стилей базы данных.
/// Используется для отладки: снимок до/после слияния позволяет отследить
/// аномалии масштабирования и коллизии стилей.
/// </summary>
internal static class DimensionStyleDiagnosticUtils
{
    private static readonly HashSet<string> StandardDimensionStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISO-25",
        "Standard",
        "Annotative"
    };

    private static readonly PropertyInfo[] DimStyleProperties = typeof(DimStyleTableRecord)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

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
        HashSet<ObjectId> usedDimensionStyleIds = CollectUsedDimensionStyleIds(db, trx);

        List<string> dimStyles = [];
        foreach (ObjectId id in dimStyleTable)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (!style.IsErased && ShouldLogDimensionStyle(style, usedDimensionStyleIds))
            {
                dimStyles.Add(FormatDimensionStyle(style));
            }
        }

        List<string> textStyles = [];
        foreach (ObjectId id in textStyleTable)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (!style.IsDependent && !style.IsErased)
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

    private static HashSet<ObjectId> CollectUsedDimensionStyleIds(Database db, Transaction trx)
    {
        HashSet<ObjectId> usedStyleIds = [];
        AddUsedDimensionStyleId(db.Dimstyle, usedStyleIds);

        BlockTable blockTable = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            if (trx.GetObject(blockId, OpenMode.ForRead, false) is not BlockTableRecord block || block.IsErased)
            {
                continue;
            }

            foreach (ObjectId entityId in block)
            {
                if (entityId.IsNull || entityId.IsErased)
                {
                    continue;
                }

                if (trx.GetObject(entityId, OpenMode.ForRead, false) is Dimension dimension)
                {
                    AddUsedDimensionStyleId(dimension.DimensionStyle, usedStyleIds);
                }
            }
        }

        return usedStyleIds;
    }

    private static void AddUsedDimensionStyleId(ObjectId styleId, HashSet<ObjectId> usedStyleIds)
    {
        if (!styleId.IsNull && !styleId.IsErased)
        {
            _ = usedStyleIds.Add(styleId);
        }
    }

    private static bool ShouldLogDimensionStyle(DimStyleTableRecord style, IReadOnlySet<ObjectId> usedDimensionStyleIds)
    {
        return !StandardDimensionStyleNames.Contains(style.Name) || usedDimensionStyleIds.Contains(style.ObjectId);
    }

    private static string FormatDimensionStyle(DimStyleTableRecord style)
    {
        string properties = string.Join(", ", DimStyleProperties.Select(p => $"{p.Name}={FormatPropertyValue(style, p)}"));

        return
            $"styleName=\"{Escape(style.Name)}\", styleHandle={style.Handle}, " +
            $"objectId={FormatObjectId(style.ObjectId)}, " +
            $"isDependent={style.IsDependent}, isResolved={ReadOptionalBool(style, "IsResolved")}, " +
            $"properties={{ {properties} }}";
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

    private static string FormatObjectId(ObjectId id)
    {
        if (id.IsNull)
        {
            return "Null";
        }

        try
        {
            return id.Handle.ToString();
        }
        catch (System.Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }

    private static string FormatPropertyValue(DimStyleTableRecord style, PropertyInfo property)
    {
        try
        {
            return FormatValue(property.GetValue(style));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return $"<error: {ex.InnerException.GetType().Name}>";
        }
        catch (System.Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{Escape(text)}\"",
            double d => F(d),
            float f => F(f),
            decimal d => d.ToString("0.######", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Color color => FormatColor(color),
            ObjectId id => FormatObjectId(id),
            Enum e => e.ToString(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => $"\"{Escape(value.ToString())}\""
        };
    }

    private static string ReadOptionalBool(DimStyleTableRecord style, string propertyName)
    {
        PropertyInfo? property = typeof(DimStyleTableRecord).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length > 0)
        {
            return "n/a";
        }

        return FormatPropertyValue(style, property);
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
