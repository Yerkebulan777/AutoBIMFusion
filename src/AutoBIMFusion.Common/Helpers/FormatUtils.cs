using System.Globalization;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.Colors;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты форматирования для AutoCAD-объектов и произвольных значений.
/// </summary>
public static class FormatUtils
{
    /// <summary>
    ///     Форматирует AutoCAD Color как "Method:Index".
    /// </summary>
    public static string FormatColor(Color color)
    {
        return $"{color.ColorMethod}:{color.ColorIndex}";
    }

    /// <summary>
    ///     Форматирует ObjectId как строку Handle; "Null" если пустой;
    ///     "&lt;error: ...&gt;" при исключении.
    /// </summary>
    public static string FormatObjectId(ObjectId id)
    {
        if (id.IsNull) return "Null";

        try
        {
            return id.Handle.ToString();
        }
        catch (Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }

    /// <summary>
    ///     Полиморфное форматирование произвольного значения в строку.
    ///     Поддерживает: null, string, double/float/decimal, bool, Color, ObjectId, Enum, IFormattable.
    /// </summary>
    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{StringUtils.EscapeForQuotedContext(text)}\"",
            double d => NumericUtils.FormatF6(d),
            float f => NumericUtils.FormatF6(f),
            decimal d => d.ToString("0.######", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Color color => FormatColor(color),
            ObjectId id => FormatObjectId(id),
            Enum e => e.ToString(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => $"\"{StringUtils.EscapeForQuotedContext(value.ToString())}\""
        };
    }

    /// <summary>
    ///     Добавляет пары key=value из словаря в StringBuilder (каждая на новой строке с отступом).
    /// </summary>
    public static void AppendProperties(StringBuilder builder, IReadOnlyDictionary<string, string> properties)
    {
        foreach (var kvp in properties) _ = builder.AppendLine($"  {kvp.Key}={kvp.Value}");
    }

    /// <summary>
    ///     Добавляет все свойства DimStyleTableRecord (через рефлексию) в StringBuilder.
    /// </summary>
    public static void AppendDimStyleProperties(StringBuilder builder, DimStyleTableRecord style)
    {
        var properties = typeof(DimStyleTableRecord).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
            _ = builder.AppendLine($"  {property.Name}={ReflectionHelper.FormatPropertyValue(style, property)}");
    }
}
