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
            _ = DimensionUtils.RemoveDimStyleOverrides(dim);
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
}
