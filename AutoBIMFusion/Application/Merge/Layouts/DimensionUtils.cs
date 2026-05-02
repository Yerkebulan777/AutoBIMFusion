using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Общие утилиты для работы с размерами AutoCAD.
/// </summary>
internal static class DimensionUtils
{
    /// <summary>
    /// Удаляет XData переопределений стиля размеров (приложение "DSTYLE").
    /// </summary>
    public static bool RemoveDimStyleOverrides(Dimension dim)
    {
        try
        {
            if (dim.XData != null)
            {
                using ResultBuffer rb = dim.XData;
                bool hasOverrides = false;
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value.ToString() == "DSTYLE")
                    {
                        hasOverrides = true;
                        break;
                    }
                }

                if (hasOverrides)
                {
                    // ResultBuffer только с именем приложения удаляет все данные этого приложения
                    using ResultBuffer clearRb = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "DSTYLE"));
                    dim.XData = clearRb;
                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(ex, "Failed to remove DSTYLE overrides for dimension {Handle}", dim.Handle);
        }
        return false;
    }

    /// <summary>
    /// Выполняет комплексную очистку и нормализацию размера.
    /// </summary>
    public static void Heal(Dimension dim)
    {
        ObjectId styleId = dim.DimensionStyle;
        bool overridesCleared = RemoveDimStyleOverrides(dim);

        if (!dim.IsWriteEnabled)
        {
            dim.UpgradeOpen();
        }

        // Сброс поворота текста, который часто сбивается при мерже/трансформации
        if (Math.Abs(dim.TextRotation) > 1e-9)
        {
            dim.TextRotation = 0.0;
        }

        // Если переопределения были очищены, принудительно восстанавливаем оригинальный стиль,
        // чтобы AutoCAD пересчитал визуальные свойства.
        if (overridesCleared && !styleId.IsNull)
        {
            dim.DimensionStyle = styleId;
        }
    }
}
