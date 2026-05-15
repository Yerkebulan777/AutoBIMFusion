using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Математические операции с габаритами AutoCAD-геометрии.
/// </summary>
public static class ExtentsUtils
{
    private const double MinValidDiagonal = 0.001;

    /// <summary>
    ///     Безопасно получает геометрические габариты сущности.
    ///     Перехватывает исключения при расчёте габаритов некорректных геометрий.
    /// </summary>
    /// <param name="ent">Сущность.</param>
    /// <returns>Габариты или null, если не удалось вычислить.</returns>
    public static Extents3d? TryGetExtents(Entity ent)
    {
        try
        {
            return ent.GeometricExtents;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Пытается вычислить отношение диагоналей габаритов после трансформации к габаритам до неё.
    /// </summary>
    /// <returns>
    ///     True, если оба набора габаритов доступны и исходная диагональ достаточно велика;
    ///     иначе false.
    /// </returns>
    public static bool TryGetScaleRatio(Extents3d? before, Extents3d? after, out double beforeDiagonal,
        out double afterDiagonal, out double ratio)
    {
        ratio = 0.0;
        beforeDiagonal = 0.0;
        afterDiagonal = 0.0;

        if (!before.HasValue || !after.HasValue)
        {
            return false;
        }

        beforeDiagonal = before.Value.MaxPoint.DistanceTo(before.Value.MinPoint);
        afterDiagonal = after.Value.MaxPoint.DistanceTo(after.Value.MinPoint);

        if (beforeDiagonal <= MinValidDiagonal)
        {
            return false;
        }

        ratio = afterDiagonal / beforeDiagonal;
        return true;
    }

    /// <summary>
    ///     Проверяет, находится ли точка внутри габаритов 3D (включая границы).
    /// </summary>
    /// <param name="extents">Габариты.</param>
    /// <param name="point">Точка.</param>
    /// <returns>True, если точка внутри; иначе false.</returns>
    public static bool IsPointIn(Extents3d extents, Point3d point)
    {
        return point.X >= extents.MinPoint.X && point.X <= extents.MaxPoint.X
                                             && point.Y >= extents.MinPoint.Y && point.Y <= extents.MaxPoint.Y
                                             && point.Z >= extents.MinPoint.Z && point.Z <= extents.MaxPoint.Z;
    }

    /// <summary>
    ///     Проверяет, находится ли базовая точка сущности (Position/Location) внутри габаритов.
    ///     Поддерживает DBText, MText, BlockReference, DBPoint.
    /// </summary>
    /// <param name="ent">Сущность.</param>
    /// <param name="bounds">Габариты.</param>
    /// <returns>True, если точка сущности внутри; иначе false.</returns>
    public static bool IsEntityPointIn(Entity ent, Extents3d bounds)
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
    ///     Проверяет пересечение двух AABB (Axis-Aligned Bounding Box) прямоугольников.
    /// </summary>
    /// <param name="a">Первые габариты.</param>
    /// <param name="b">Вторые габариты.</param>
    /// <returns>True, если прямоугольники пересекаются или касаются; иначе false.</returns>
    public static bool AabbIntersect(Extents3d a, Extents3d b)
    {
        return a.MinPoint.X <= b.MaxPoint.X
               && a.MaxPoint.X >= b.MinPoint.X
               && a.MinPoint.Y <= b.MaxPoint.Y
               && a.MaxPoint.Y >= b.MinPoint.Y;
    }

    /// <summary>
    ///     Объединяет два набора габаритов в один, охватывающий оба.
    /// </summary>
    /// <param name="a">Первые габариты.</param>
    /// <param name="b">Вторые габариты.</param>
    /// <returns>Объединённые габариты.</returns>
    public static Extents3d Union(Extents3d a, Extents3d b)
    {
        Point3d min = new(
            Min(a.MinPoint.X, b.MinPoint.X),
            Min(a.MinPoint.Y, b.MinPoint.Y),
            Min(a.MinPoint.Z, b.MinPoint.Z));

        Point3d max = new(
            Max(a.MaxPoint.X, b.MaxPoint.X),
            Max(a.MaxPoint.Y, b.MaxPoint.Y),
            Max(a.MaxPoint.Z, b.MaxPoint.Z));

        return new Extents3d(min, max);
    }

    /// <summary>
    ///     Получает габариты всей базы данных.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    /// <returns>Габариты или null, если база пуста или некорректна.</returns>
    public static Extents3d? GetDatabaseExtents(Database db)
    {
        try
        {
            db.UpdateExt(true);
            Point3d min = db.Extmin;
            Point3d max = db.Extmax;

            // В AutoCAD, если база пуста, Extmin > Extmax
            return min.X > max.X || min.Y > max.Y || min.Z > max.Z ? null : new Extents3d(min, max);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Трансформирует габариты с помощью матрицы.
    /// </summary>
    /// <param name="ext">Габариты.</param>
    /// <param name="mat">Матрица трансформации.</param>
    /// <returns>Новые габариты.</returns>
    public static Extents3d Transform(Extents3d ext, Matrix3d mat)
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
            minX = Min(minX, p.X);
            minY = Min(minY, p.Y);
            minZ = Min(minZ, p.Z);
            maxX = Max(maxX, p.X);
            maxY = Max(maxY, p.Y);
            maxZ = Max(maxZ, p.Z);
        }

        return new Extents3d(new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
    }

    /// <summary>
    ///     Форматирует габариты для логирования/отладки.
    ///     Пример: "[(-10.123, -20.456, 0.000) -> (100.789, 200.345, 50.000)]"
    /// </summary>
    /// <param name="ext">Габариты.</param>
    /// <returns>Форматированная строка.</returns>
    public static string FormatExtents(Extents3d ext)
    {
        return $"[{FormatPoint(ext.MinPoint)} -> {FormatPoint(ext.MaxPoint)}]";
    }

    /// <summary>
    ///     Форматирует 3D точку для логирования/отладки.
    ///     Пример: "(-10.123, -20.456, 0.000)"
    /// </summary>
    /// <param name="p">Точка.</param>
    /// <returns>Форматированная строка с тремя знаками после запятой.</returns>
    public static string FormatPoint(Point3d p)
    {
        return $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
    }

    /// <summary>
    ///     Нормализует единицы измерения базы данных к миллиметрам и метрической системе.
    ///     Единая точка синхронизации единиц для всего пайплайна слияния.
    ///     MEASUREINIT намеренно не меняется: это registry-переменная для новых чертежей, а не состояние source DB.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    public static void SyncUnits(Database db)
    {
        if (db.Insunits != UnitsValue.Millimeters)
        {
            db.Insunits = UnitsValue.Millimeters;
        }

        if (db.Measurement != MeasurementValue.Metric)
        {
            db.Measurement = MeasurementValue.Metric;
        }
    }

    /// <summary>
    ///     Проверяет примерное равенство двух габаритов AABB с заданной точностью.
    ///     Сравнивает только координаты X и Y.
    /// </summary>
    public static bool ExtentsApproxEqual(Extents3d a, Extents3d b, double tolerance = 1e-6)
    {
        return Abs(a.MinPoint.X - b.MinPoint.X) <= tolerance
               && Abs(a.MinPoint.Y - b.MinPoint.Y) <= tolerance
               && Abs(a.MaxPoint.X - b.MaxPoint.X) <= tolerance
               && Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tolerance;
    }

    /// <summary>
    ///     Вычисляет габариты Model Space прямым сканированием сущностей, минуя кэшированные
    ///     значения <see cref="Database.Extmin"/>/<see cref="Database.Extmax"/>.
    ///     Надёжен на headless-базах после операций удаления, когда <see cref="Database.UpdateExt"/>
    ///     может возвращать устаревший результат.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    /// <returns>Объединённые габариты всех видимых сущностей Model Space, или null если пространство пусто.</returns>
    public static Extents3d? ComputeModelSpaceBounds(Database db)
    {
        Extents3d? result = null;

        using Transaction trx = db.TransactionManager.StartTransaction();

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        var ms = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            if (!id.IsValid || id.IsErased)
                continue;

            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
                continue;

            Extents3d? ext = TryGetExtents(ent);
            if (ext is null)
                continue;

            result = result is null ? ext.Value : Union(result.Value, ext.Value);
        }

        trx.Commit();
        return result;
    }

    /// <summary>
    ///     Вычисляет объединённый AABB коллекции ObjectId.
    ///     Возвращает null, если ни один объект не имеет валидных габаритов.
    /// </summary>
    public static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds)
    {
        if (entityIds.Count == 0) return null;

        Extents3d? acc = null;
        using var trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in entityIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

            var ext = TryGetExtents(ent);
            if (ext is null) continue;

            acc = acc is null ? ext.Value : Union(acc.Value, ext.Value);
        }

        trx.Commit();
        return acc;
    }
}
