namespace AutoBIMFusion.Application.Merge.Layouts.Transforms;

/// <summary>
/// Применяет преобразования объектов AutoCAD с необходимой постобработкой для
/// многолинейных линеек и штриховки.
/// </summary>
internal static class EntityTransformUtils
{
    internal readonly record struct TransformResult(bool Transformed, bool SkippedAssociativeHatch);

    internal static TransformResult TransformEntity(Entity entity, Matrix3d matrix)
    {
        if (entity is Hatch { Associative: true })
        {
            return new TransformResult(false, true);
        }

        entity.TransformBy(matrix);

        if (entity is Hatch hatch)
        {
            EvaluateHatch(hatch);
        }

        // --- ИСПРАВЛЕНИЕ: ЖЕСТКАЯ ОЧИСТКА ПЕРЕОПРЕДЕЛЕНИЙ РАЗМЕРОВ ---
        if (entity is Dimension dim)
        {
            RemoveDimStyleOverrides(dim);
        }
        // -------------------------------------------------------------

        return new TransformResult(true, false);
    }

    private static void EvaluateHatch(Hatch hatch)
    {
        try
        {
            hatch.EvaluateHatch(true);
        }
        catch
        {
            // AutoCAD can reject evaluation for damaged or very complex hatch geometry.
        }
    }

    // Удаляем XData переопределений стиля размеров (приложение "DSTYLE")
    private static void RemoveDimStyleOverrides(Dimension dim)
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
                    using ResultBuffer clearRb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "DSTYLE"));
                    dim.XData = clearRb;
                }
            }
        }
        catch
        {
            // Игнорируем ошибки при очистке, чтобы не прерывать процесс
        }
    }
}
