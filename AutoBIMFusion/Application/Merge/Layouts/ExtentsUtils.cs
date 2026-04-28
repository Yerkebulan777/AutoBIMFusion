namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Математические операции с габаритами AutoCAD-геометрии.
/// </summary>
internal static class ExtentsUtils
{
    /// <summary>
    /// Безопасно получает геометрические габариты сущности.
    /// Перехватывает исключения при расчёте габаритов некорректных геометрий.
    /// </summary>
    /// <param name="ent">Сущность.</param>
    /// <returns>Габариты или null, если не удалось вычислить.</returns>
    internal static Extents3d? TryGetExtents(Entity ent)
    {
        try
        {
            return ent.GeometricExtents;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Проверяет, находится ли точка внутри габаритов 3D (включая границы).
    /// </summary>
    /// <param name="extents">Габариты.</param>
    /// <param name="point">Точка.</param>
    /// <returns>True, если точка внутри; иначе false.</returns>
    internal static bool IsPointIn(Extents3d extents, Point3d point)
    {
        return point.X >= extents.MinPoint.X && point.X <= extents.MaxPoint.X
            && point.Y >= extents.MinPoint.Y && point.Y <= extents.MaxPoint.Y
            && point.Z >= extents.MinPoint.Z && point.Z <= extents.MaxPoint.Z;
    }

    /// <summary>
    /// Проверяет, находится ли базовая точка сущности (Position/Location) внутри габаритов.
    /// Поддерживает DBText, MText, BlockReference, DBPoint.
    /// </summary>
    /// <param name="ent">Сущность.</param>
    /// <param name="bounds">Габариты.</param>
    /// <returns>True, если точка сущности внутри; иначе false.</returns>
    internal static bool IsEntityPointIn(Entity ent, Extents3d bounds)
    {
        Point3d? p = ent switch
        {
            MText m => m.Location,
            DBText t => t.Position,
            BlockReference br => br.Position,
            DBPoint dbPoint => dbPoint.Position,
            _ => null
        };

        return p.HasValue && IsPointIn(bounds, p.Value);
    }

    /// <summary>
    /// Проверяет пересечение двух AABB (Axis-Aligned Bounding Box) прямоугольников.
    /// </summary>
    /// <param name="a">Первые габариты.</param>
    /// <param name="b">Вторые габариты.</param>
    /// <returns>True, если прямоугольники пересекаются; иначе false.</returns>
    internal static bool AabbIntersect(Extents3d a, Extents3d b)
    {
        return a.MinPoint.X <= b.MaxPoint.X
            && a.MaxPoint.X >= b.MinPoint.X
            && a.MinPoint.Y <= b.MaxPoint.Y
            && a.MaxPoint.Y >= b.MinPoint.Y;
    }

    /// <summary>
    /// Объединяет два набора габаритов в один, охватывающий оба.
    /// </summary>
    /// <param name="a">Первые габариты.</param>
    /// <param name="b">Вторые габариты.</param>
    /// <returns>Объединённые габариты.</returns>
    internal static Extents3d Union(Extents3d a, Extents3d b)
    {
        Point3d min = new(
            Math.Min(a.MinPoint.X, b.MinPoint.X),
            Math.Min(a.MinPoint.Y, b.MinPoint.Y),
            Math.Min(a.MinPoint.Z, b.MinPoint.Z));

        Point3d max = new(
            Math.Max(a.MaxPoint.X, b.MaxPoint.X),
            Math.Max(a.MaxPoint.Y, b.MaxPoint.Y),
            Math.Max(a.MaxPoint.Z, b.MaxPoint.Z));

        return new Extents3d(min, max);
    }

    /// <summary>
    /// Получает габариты всей базы данных.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    /// <returns>Габариты или null, если база пуста или некорректна.</returns>
    internal static Extents3d? GetDatabaseExtents(Database db)
    {
        try
        {
            db.UpdateExt(true);
            Point3d min = db.Extmin;
            Point3d max = db.Extmax;

            // В AutoCAD, если база пуста, Extmin > Extmax
            return min.X > max.X || min.Y > max.Y || min.Z > max.Z ? null : new Extents3d(min, max);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Трансформирует габариты с помощью матрицы.
    /// </summary>
    /// <param name="ext">Габариты.</param>
    /// <param name="mat">Матрица трансформации.</param>
    /// <returns>Новые габариты.</returns>
    internal static Extents3d Transform(Extents3d ext, Matrix3d mat)
    {
        Span<Point3d> corners =
        [
            ext.MinPoint,
            new(ext.MinPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
            new(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
            new(ext.MaxPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
            ext.MaxPoint,
            new(ext.MinPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
            new(ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
            new(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z)
        ];

        Point3d first = corners[0].TransformBy(mat);
        double minX = first.X;
        double minY = first.Y;
        double minZ = first.Z;
        double maxX = first.X;
        double maxY = first.Y;
        double maxZ = first.Z;

        for (int i = 1; i < corners.Length; i++)
        {
            Point3d p = corners[i].TransformBy(mat);
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
            maxZ = Math.Max(maxZ, p.Z);
        }

        return new Extents3d(new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
    }

    /// <summary>
    /// Форматирует габариты для логирования/отладки.
    /// Пример: "[(-10.123, -20.456, 0.000) -> (100.789, 200.345, 50.000)]"
    /// </summary>
    /// <param name="ext">Габариты.</param>
    /// <returns>Форматированная строка.</returns>
    internal static string FormatExtents(Extents3d ext)
    {
        return $"[{FormatPoint(ext.MinPoint)} -> {FormatPoint(ext.MaxPoint)}]";
    }

    /// <summary>
    /// Форматирует 3D точку для логирования/отладки.
    /// Пример: "(-10.123, -20.456, 0.000)"
    /// </summary>
    /// <param name="p">Точка.</param>
    /// <returns>Форматированная строка с тремя знаками после запятой.</returns>
    internal static string FormatPoint(Point3d p)
    {
        return $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
    }
}
