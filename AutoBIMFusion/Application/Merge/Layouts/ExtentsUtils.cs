namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Утилитарный класс для работы с габаритами (Extents3d и Extents2d).
/// Предоставляет методы для вычисления, преобразования и анализа границ геометрических объектов.
/// Соблюдает принцип Single Responsibility: отвечает только за математику габаритов.
/// </summary>
internal static class ExtentsUtils
{
    /// <summary>
    /// Получает объединённые габариты для коллекции сущностей.
    /// </summary>
    /// <param name="entities">Коллекция сущностей.</param>
    /// <returns>Объединённые габариты или null, если коллекция пуста.</returns>
    internal static Extents3d? GetExtents(IEnumerable<Entity> entities)
    {
        List<Entity> entityList = entities.ToList();
        if (entityList.Count == 0)
        {
            return null;
        }

        Extents3d result = TryGetExtents(entityList[0]) ?? new Extents3d();

        foreach (Entity? ent in entityList.Skip(1))
        {
            Extents3d? ext = TryGetExtents(ent);
            if (ext.HasValue)
            {
                result.AddExtents(ext.Value);
            }
        }

        return result;
    }

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
    /// Получает геометрический центр габаритов 3D.
    /// </summary>
    /// <param name="extents">Габариты.</param>
    /// <returns>Центральная точка.</returns>
    internal static Point3d GetCenter(Extents3d extents)
    {
        return Point3d.Origin + (0.5 * (extents.MinPoint.GetAsVector() + extents.MaxPoint.GetAsVector()));
    }

    /// <summary>
    /// Получает геометрический центр габаритов 2D.
    /// </summary>
    /// <param name="extents">Габариты.</param>
    /// <returns>Центральная точка.</returns>
    internal static Point2d GetCenter(Extents2d extents)
    {
        return Point2d.Origin + (0.5 * (extents.MinPoint.GetAsVector() + extents.MaxPoint.GetAsVector()));
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
    /// Проверяет, находится ли точка внутри габаритов 2D (включая границы).
    /// </summary>
    /// <param name="extents">Габариты.</param>
    /// <param name="point">Точка.</param>
    /// <returns>True, если точка внутри; иначе false.</returns>
    internal static bool IsPointIn(Extents2d extents, Point2d point)
    {
        return point.X >= extents.MinPoint.X && point.X <= extents.MaxPoint.X
            && point.Y >= extents.MinPoint.Y && point.Y <= extents.MaxPoint.Y;
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
    /// Преобразует габариты 3D в габариты 2D.
    /// По умолчанию проецирует на плоскость XY.
    /// </summary>
    /// <param name="extents">Габариты 3D.</param>
    /// <param name="x">Функция для вычисления X координаты (по умолчанию использует X из 3D).</param>
    /// <param name="y">Функция для вычисления Y координаты (по умолчанию использует Y из 3D).</param>
    /// <returns>Габариты 2D.</returns>
    internal static Extents2d ToExtents2d(
        Extents3d extents,
        Func<Point3d, double>? x = null,
        Func<Point3d, double>? y = null)
    {
        x ??= p => p.X;
        y ??= p => p.Y;

        return new Extents2d(
            x(extents.MinPoint),
            y(extents.MinPoint),
            x(extents.MaxPoint),
            y(extents.MaxPoint));
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
        Point3d[] corners = new Point3d[]
        {
            ext.MinPoint,
            new(ext.MinPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
            new(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
            new(ext.MaxPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
            ext.MaxPoint,
            new(ext.MinPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
            new(ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
            new(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z)
        };

        List<Point3d> transformedCorners = corners.Select(p => p.TransformBy(mat)).ToList();
        Point3d min = new(
            transformedCorners.Min(p => p.X),
            transformedCorners.Min(p => p.Y),
            transformedCorners.Min(p => p.Z));
        Point3d max = new(
            transformedCorners.Max(p => p.X),
            transformedCorners.Max(p => p.Y),
            transformedCorners.Max(p => p.Z));

        return new Extents3d(min, max);
    }

    /// <summary>
    /// Получает габариты 2D для коллекции сущностей.
    /// </summary>
    /// <param name="entities">Коллекция сущностей.</param>
    /// <returns>Габариты 2D или null.</returns>
    internal static Extents2d? GetExtents2d(IEnumerable<Entity> entities)
    {
        Extents3d? ext3d = GetExtents(entities);
        return ext3d.HasValue ? ToExtents2d(ext3d.Value) : null;
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
