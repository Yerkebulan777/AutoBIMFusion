namespace AutoBIMFusion.Application.Combine.Layouts;

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

        // Размеры получают Viewport-стиль и свой линейный масштаб до клонирования.

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
