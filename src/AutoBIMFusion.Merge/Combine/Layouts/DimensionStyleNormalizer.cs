using System.Collections.Generic;
using AutoBIMFusion.AutoCAD.AcadSupport;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutoBIMFusion.Merge.Layouts;

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
            if (!pair.IsCloned || !pair.Value.IsValidForOperation())
                continue;

            DBObject obj = trx.GetObject(pair.Value, OpenMode.ForWrite, false);

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
                    clonedStyleIds.Add(pair.Value);
                    break;
            }
        }

        // Проход 2: для размеров чей стиль НЕ был клонирован
        // применяем масштаб как per-dimension DVAR поверх существующего стиля
        // SetDimstyleData здесь не вызываем — Dimtxt не меняется → TextPosition валидна
        HashSet<ObjectId> processedDims = [];

        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || !pair.IsPrimary || !pair.Value.IsValidForOperation())
                continue;

            if (!processedDims.Add(pair.Value))
                continue;

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim)
                continue;

            bool needsRecompute = false;

            // 1. Force text rotation to 0 to remove any overrides
            if (dim.TextRotation != 0.0)
            {
                dim.TextRotation = 0.0;
                needsRecompute = true;
            }

            // 2. Apply scale overrides if the style was NOT cloned
            if (!clonedStyleIds.Contains(dim.DimensionStyle))
            {
                dim.Dimscale = targetVisualScale;
                dim.Dimlfac = linearScaleMultiplier;
                needsRecompute = true;
            }

            // 3. Recompute if any changes were made
            if (needsRecompute)
            {
                dim.RecomputeDimensionBlock(true);
            }
        }
    }
}
