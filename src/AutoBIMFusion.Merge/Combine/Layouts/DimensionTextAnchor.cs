using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutoBIMFusion.Merge.Layouts;

/// <summary>
/// Геометрический сброс позиции текста размера.
///
/// ПРОБЛЕМА: после WblockCloneObjects + TransformBy TextPosition (DXF code 11)
/// хранит абсолютную WCS-точку из source-файла. При смене стиля (Dimtxt/Dimtad/Dimgap)
/// AutoCAD интерпретирует её как смещение — текст "улетает в бок".
///
/// ПОЧЕМУ UsingDefaultTextPosition НЕ ПОМОГАЕТ: флаг не очищает DXF code 11;
/// в ряде версий AutoCAD кэшированная stale-координата используется
/// как базовая точка даже при UsingDefaultTextPosition = true.
///
/// РЕШЕНИЕ: вычислить середину размерной линии из контрольных точек самостоятельно
/// и записать её как TextPosition до вызова RecomputeDimensionBlock.
/// DXF code 11 будет содержать геометрически корректную точку —
/// AutoCAD не будет искать "потерянный" текст в стороне.
/// </summary>
internal static class DimensionTextAnchor
{
    /// <summary>
    /// Привязывает текст размера к геометрической середине размерной линии.
    /// Заменяет паттерн UsingDefaultTextPosition = false → true.
    /// Вызывать ПОСЛЕ SetDimstyleData и dim.Dimtmove = 0, ДО RecomputeDimensionBlock.
    /// </summary>
    internal static void AnchorTextToMidpoint(Dimension dim)
    {
        Point3d mid = ComputeDimLineMidpoint(dim);

        // Принудительно записываем геометрически корректную точку в DXF code 11.
        // Это перезаписывает stale-координату из source-файла.
        dim.TextPosition = mid;

        // Теперь флаг сбрасывается надёжно — AutoCAD отталкивается от валидной точки.
        dim.UsingDefaultTextPosition = false;
        dim.UsingDefaultTextPosition = true;
    }

    /// <summary>
    /// Вычисляет середину размерной линии из контрольных точек.
    ///
    /// Алгоритм для RotatedDimension/AlignedDimension:
    ///   1. Проецируем XLine1/2Point на ось размерной линии → получаем t1, t2
    ///   2. tMid = (t1 + t2) / 2 — середина вдоль оси
    ///   3. Перпендикулярное смещение берём из DimLinePoint (не из XLine-точек)
    ///   4. Собираем результирующую точку в WCS
    ///
    /// Для всех прочих типов — возвращаем DimLinePoint как наилучшее приближение.
    /// </summary>
    private static Point3d ComputeDimLineMidpoint(Dimension dim)
    {
        if (dim is RotatedDimension rot)
            return ComputeRotatedMidpoint(rot);

        if (dim is AlignedDimension aln)
            return ComputeAlignedMidpoint(aln);

        // OrdinateDimension, RadialDimension, DiametricDimension — оставляем TextPosition как есть,
        // т.к. у них нет классической "размерной линии" между двумя выносными.
        return dim.TextPosition;
    }

    private static Point3d ComputeRotatedMidpoint(RotatedDimension rot)
    {
        double angle = rot.Rotation;
        // Единичные векторы вдоль и поперёк размерной линии
        Vector3d along = new(Math.Cos(angle), Math.Sin(angle), 0.0);
        Vector3d perp  = new(-Math.Sin(angle), Math.Cos(angle), 0.0);

        // Проекция выносных точек на ось размерной линии
        double t1   = along.DotProduct(rot.XLine1Point.GetAsVector());
        double t2   = along.DotProduct(rot.XLine2Point.GetAsVector());
        double tMid = (t1 + t2) * 0.5;

        // Перпендикулярный отступ берём из DimLinePoint (пользователь задал его при создании)
        double perpOff = perp.DotProduct(rot.DimLinePoint.GetAsVector());

        return new Point3d(
            tMid * along.X + perpOff * perp.X,
            tMid * along.Y + perpOff * perp.Y,
            0.0);
    }

    private static Point3d ComputeAlignedMidpoint(AlignedDimension aln)
    {
        Vector3d seg = aln.XLine2Point - aln.XLine1Point;

        // Вырожденный случай — выносные совпадают
        if (seg.Length < 1e-10)
            return aln.DimLinePoint;

        Vector3d along = seg.GetNormal();
        Vector3d perp  = new(-along.Y, along.X, 0.0);

        double t1   = along.DotProduct(aln.XLine1Point.GetAsVector());
        double t2   = along.DotProduct(aln.XLine2Point.GetAsVector());
        double tMid = (t1 + t2) * 0.5;

        double perpOff = perp.DotProduct(aln.DimLinePoint.GetAsVector());

        return new Point3d(
            tMid * along.X + perpOff * perp.X,
            tMid * along.Y + perpOff * perp.Y,
            0.0);
    }
}
