namespace AutoBIMFusion.AutoCAD.Helpers;

/// <summary>
/// Применяет преобразования объектов AutoCAD с необходимой постобработкой для
/// многолинейных линеек и штриховки.
/// </summary>
public static class EntityTransformUtils
{
    public readonly record struct TransformResult(bool Transformed, bool SkippedAssociativeHatch);

    public static TransformResult TransformEntity(Entity entity, Matrix3d matrix, Transaction? trx = null)
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
        else if (entity is Dimension dim)
        {
            // Reset any explicit text rotation applied by the matrix transformation
            dim.TextRotation = 0.0;
        }
        else if (entity is BlockReference blockRef && trx != null)
        {
            foreach (ObjectId attrId in blockRef.AttributeCollection)
            {
                if (!attrId.IsNull && !attrId.IsErased)
                {
                    if (trx.GetObject(attrId, OpenMode.ForWrite) is AttributeReference attr)
                    {
                        // Принудительный пересчет ширины для многострочных атрибутов
                        if (attr.IsMTextAttribute)
                        {
                            attr.UpdateMTextAttribute();
                        }
                        // Восстановление базового выравнивания после применения матрицы
                        attr.AdjustAlignment(entity.Database);
                    }
                }
            }
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
