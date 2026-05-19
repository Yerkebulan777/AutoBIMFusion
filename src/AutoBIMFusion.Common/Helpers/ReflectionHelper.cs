using System.Globalization;
using System.Reflection;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты рефлексии: безопасная установка значений, конвертация типов,
///     загрузка сборок, форматирование свойств.
/// </summary>
public static class ReflectionHelper
{
    /// <summary>
    ///     Устанавливает значение свойства или поля по имени через рефлексию
    ///     с автоматической конвертацией типа.
    /// </summary>
    /// <param name="target">Целевой объект.</param>
    /// <param name="memberName">Имя свойства или поля (case-insensitive).</param>
    /// <param name="value">Значение для установки.</param>
    /// <param name="required">Если true, выбрасывает MissingMemberException при отсутствии члена.</param>
    /// <exception cref="MissingMemberException">Член не найден и required=true.</exception>
    public static void SetMemberValue(object target, string memberName, object value, bool required = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

        var targetType = target.GetType();

        var property = targetType.GetProperty(memberName, flags);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(target, ConvertMemberValue(value, property.PropertyType));
            return;
        }

        var field = targetType.GetField(memberName, flags);
        if (field is not null)
        {
            field.SetValue(target, ConvertMemberValue(value, field.FieldType));
            return;
        }

        if (required) throw new MissingMemberException(targetType.FullName, memberName);
    }

    /// <summary>
    ///     Конвертирует значение к целевому типу с поддержкой:
    ///     bool↔int, enum, Nullable, IConvertible.
    /// </summary>
    public static object? ConvertMemberValue(object value, Type targetType)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return effectiveType == typeof(bool) && value is int intValue
            ? intValue != 0
            : effectiveType == typeof(int) && value is bool boolValue
                ? boolValue ? 1 : 0
                : effectiveType.IsEnum
                    ? Enum.ToObject(effectiveType, value)
                    : effectiveType == value.GetType()
                        ? value
                        : Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Безопасно получает типы из сборки, обрабатывая ReflectionTypeLoadException.
    /// </summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    ///     Безопасно получает и форматирует значение свойства через рефлексию.
    /// </summary>
    public static string FormatPropertyValue(object target, PropertyInfo property)
    {
        try
        {
            return FormatUtils.FormatValue(property.GetValue(target));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return $"<error: {ex.InnerException.GetType().Name}>";
        }
        catch (Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }
}
