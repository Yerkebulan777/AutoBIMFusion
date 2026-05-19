using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal static class DimensionStyleNormalizer
{
    internal static void NormalizeClonedStyles(
        IdMapping idMap,
        Transaction trx,
        double targetVisualScale,
        double linearScaleMultiplier)
    {
        // Проход 1: патчим клонированные стили — размеры не трогаем
        HashSet<ObjectId> clonedStyleIds = [];

        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || !pair.Value.IsValidForOperation()) continue;

            var obj = trx.GetObject(pair.Value, OpenMode.ForWrite, false);

            switch (obj)
            {
                // TextSize = 0 обязателен: иначе AutoCAD игнорирует Dimtxt
                // и умножает TextSize напрямую на Dimscale → неверный масштаб текста
                case TextStyleTableRecord ts:
                    ts.TextSize = 0.0;
                    break;

                case DimStyleTableRecord ds:
                    ds.Dimscale = targetVisualScale;
                    ds.Dimlfac = linearScaleMultiplier;
                    _ = clonedStyleIds.Add(pair.Value);
                    break;
            }
        }

        // Проход 2: для размеров чей стиль НЕ был клонирован
        // применяем масштаб как per-dimension DVAR поверх существующего стиля
        // SetDimstyleData здесь не вызываем — Dimtxt не меняется → TextPosition валидна
        HashSet<ObjectId> processedDims = [];

        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || !pair.IsPrimary || !pair.Value.IsValidForOperation()) continue;

            if (!processedDims.Add(pair.Value)) continue;

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim) continue;

            var needsRecompute = false;

            // 1. Принудительно сбрасываем поворот текста в 0 для удаления переопределений
            if (dim.TextRotation != 0.0)
            {
                dim.TextRotation = 0.0;
                needsRecompute = true;
            }

            // 2. Применяем переопределения масштаба, если стиль НЕ был клонирован
            if (!clonedStyleIds.Contains(dim.DimensionStyle))
            {
                dim.Dimscale = targetVisualScale;
                dim.Dimlfac = linearScaleMultiplier;
                needsRecompute = true;
            }

            // 3. Пересчитываем, если были внесены изменения
            if (needsRecompute) dim.RecomputeDimensionBlock(true);
        }
    }
}
