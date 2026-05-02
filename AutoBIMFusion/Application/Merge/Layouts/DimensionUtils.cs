using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Утилиты для работы с размерами AutoCAD.
/// </summary>
internal static class DimensionUtils
{
    /// <summary>
    /// Удаляет переопределения размерного стиля (DSTYLE), хранящиеся в XData объекта.
    /// </summary>
    /// <param name="dim">Объект размера.</param>
    /// <returns>True, если переопределения были найдены и удалены.</returns>
    internal static bool TryRemoveDimensionStyleOverrides(Dimension dim)
    {
        try
        {
            if (dim.XData == null) return false;

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
                using ResultBuffer clearRb = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "DSTYLE"));
                dim.XData = clearRb;
                return true;
            }
        }
        catch (System.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(ex, "Не удалось удалить переопределения DSTYLE для размера {Handle}", dim.Handle);
        }

        return false;
    }
}
