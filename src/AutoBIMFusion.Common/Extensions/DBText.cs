namespace AutoBIMFusion.Common.Extensions;

public static class DBTextExtensions
{
    //From https://www.keanw.com/2011/02/gathering-points-defining-2d-autocad-geometry-using-net.html
    public static void ExtractBounds(this DBText txt, Point3dCollection pts)
    {
        // Используем специальный подход для объектов DBText и
        // AttributeReference, так как хотим получить все четыре угла
        // ограничивающей рамки, даже когда текст или содержащий его
        // блок повёрнут

        if (txt.Bounds.HasValue && txt.Visible)
        {
            // Создаём прямую версию текстового объекта
            // и копируем все соответствующие свойства
            // (перестали копировать AlignmentPoint, так как это
            // иногда вызывало ошибку eNotApplicable)
            // Создадим текст в начале WCS-координат
            // без поворота, чтобы проще использовать его габариты
            DBText txt2 = new()
            {
                Normal = Vector3d.ZAxis,
                Position = Point3d.Origin,
                TextString = txt.TextString,
                TextStyleId = txt.TextStyleId,
                LineWeight = txt.LineWeight,
                Thickness = txt.Thickness,
                HorizontalMode = txt.HorizontalMode,
                VerticalMode = txt.VerticalMode,
                WidthFactor = txt.WidthFactor,
                Height = txt.Height,
                IsMirroredInX = txt.IsMirroredInX,
                IsMirroredInY = txt.IsMirroredInY,
                Oblique = txt.Oblique
            };

            // Получаем его габариты, если они определены
            // (должны быть, раз у оригинала они есть)
            if (txt2.Bounds.HasValue)
            {
                Point3d maxPt = txt2.Bounds.Value.MaxPoint;
                // Размещаем все четыре угла ограничивающей рамки
                // в массиве
                Point2d[] bounds = new[]
                {
                    Point2d.Origin, new Point2d(0.0, maxPt.Y), new Point2d(maxPt.X, maxPt.Y), new Point2d(maxPt.X, 0.0)
                };

                // Будем получать WCS-координаты каждой точки
                // используя плоскость, на которой находится текст
                Plane pl = new(txt.Position, txt.Normal);

                // Поворачиваем каждую точку и добавляем её WCS-расположение в коллекцию

                foreach (Point2d pt in bounds)
                {
                    _ = pts?.Add(pl.EvaluatePoint(pt.RotateBy(txt.Rotation, Point2d.Origin)));
                }
            }
        }
    }
}
