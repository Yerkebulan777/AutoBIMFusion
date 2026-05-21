using Autodesk.AutoCAD.BoundaryRepresentation;

namespace AutoBIMFusion.Common.Extensions;

public static class RegionsExtensions
{
    public static DBObjectCollection GetPolylines(this Region reg)
    {
        // Вернём коллекцию сущностей
        // (должна включать замкнутые полилинии и другие
        // замкнутые кривые, такие как окружности)
        DBObjectCollection res = [];
        // Взрыв области → коллекция кривых / областей
        DBObjectCollection cvs = [];
        reg.Explode(cvs);

        // Создаём плоскость для преобразования 3D-координат
        // в систему координат области
        Plane pl = new(new Point3d(0, 0, 0), reg.Normal);

        using (pl)
        {
            bool finished = false;
            while (!finished && cvs.Count > 0)
            {
                // Считаем количество кривых и не-кривых, находим
                // индекс первой кривой в коллекции
                int cvCnt = 0, nonCvCnt = 0, fstCvIdx = -1;
                for (int i = 0; i < cvs.Count; i++)
                {
                    Curve? tmpCv = cvs[i] as Curve;
                    if (tmpCv == null)
                    {
                        nonCvCnt++;
                    }
                    else
                    {
                        // Замкнутые кривые можно сразу добавить
                        // в коллекцию результатов, не добавляя
                        // их в счётчик кривых
                        if (tmpCv.Closed)
                        {
                            _ = res.Add(tmpCv);
                            cvs.Remove(tmpCv);
                            // Декремент, чтобы не пропустить элемент
                            i--;
                        }
                        else
                        {
                            cvCnt++;
                            if (fstCvIdx == -1)
                            {
                                fstCvIdx = i;
                            }
                        }
                    }
                }

                if (fstCvIdx >= 0)
                {
                    // Для начального сегмента берём первую
                    // кривую из коллекции

                    Curve fstCv = (Curve)cvs[fstCvIdx];
                    // Результирующая полилиния
                    Polyline p = new();
                    // Задаём общие свойства сущности из области
                    p.SetPropertiesFrom(reg);
                    // Добавляем первые две вершины, но устанавливаем выпуклость только на первой (вторая будет установлена ретроспективно из второго сегмента)
                    // Также предполагаем, что первый сегмент идёт против часовой стрелки (по умолчанию для дуг), так как не меняем порядок вершин для соответствия порядку полилинии

                    p.AddVertexAt(p.NumberOfVertices, fstCv.StartPoint.Convert2d(pl), BulgeFromCurve(fstCv, false), 0,
                        0);

                    p.AddVertexAt(p.NumberOfVertices, fstCv.EndPoint.Convert2d(pl), 0, 0, 0);
                    cvs.Remove(fstCv);
                    // Следующая точка для поиска
                    var nextPt = fstCv.EndPoint;
                    // Находим линию, соединённую со следующей точкой
                    // Если по какой-то причине возвращённые линии не соединены, можем зациклиться.
                    // Поэтому сохраняем предыдущее количество кривых и предполагаем, что если оно не уменьшилось после полного прохода по сегментам, то не следует продолжать.
                    // Надеемся, что этого никогда не произойдёт, так как кривые должны образовывать замкнутый контур, но всё же...
                    // Устанавливаем предыдущее количество искусственно высоким, чтобы хотя бы один проход состоялся.
                    int prevCnt = cvs.Count + 1;
                    while (cvs.Count > nonCvCnt && cvs.Count < prevCnt)
                    {
                        prevCnt = cvs.Count;
                        foreach (DBObject obj in cvs)
                        {
                            Curve? cv = obj as Curve;
                            if (cv != null)
                            {
                                // Если один конец кривой соединяется с искомой точкой...
                                if (cv.StartPoint == nextPt || cv.EndPoint == nextPt)
                                {
                                    // Вычисляем выпуклость для кривой и устанавливаем её на предыдущей вершине
                                    double bulge = BulgeFromCurve(cv, cv.EndPoint == nextPt);
                                    if (bulge != 0.0)
                                    {
                                        p.SetBulgeAt(p.NumberOfVertices - 1, bulge);
                                    }

                                    // Разворачиваем точки, если нужно
                                    if (cv.StartPoint == nextPt)
                                    {
                                        nextPt = cv.EndPoint;
                                    }
                                    else
                                    {
                                        // cv.EndPoint == nextPt
                                        nextPt = cv.StartPoint;
                                    }

                                    // Добавляем новую вершину (выпуклость будет установлена в следующий раз, если нужно)
                                    p.AddVertexAt(p.NumberOfVertices, nextPt.Convert2d(pl), 0, 0, 0);
                                    // Удаляем кривую из списка, что, конечно, уменьшает счётчик
                                    cvs.Remove(cv);
                                    break;
                                }
                            }
                        }
                    }

                    // После добавления всех вершин полилинии преобразуем её в исходную плоскость области
                    p.TransformBy(Matrix3d.PlaneToWorld(pl));
                    _ = res.Add(p);
                    if (cvs.Count == nonCvCnt)
                    {
                        finished = true;
                    }
                }

                // Если в коллекции есть области, рекурсивно взрываем и добавляем их геометрию
                if (nonCvCnt > 0 && cvs.Count > 0)
                {
                    foreach (DBObject obj in cvs)
                    {
                        Region? subReg = obj as Region;
                        if (subReg != null)
                        {
                            var subRes =
                                subReg.GetPolylines();
                            foreach (DBObject o in subRes)
                            {
                                _ = res.Add(o);
                            }

                            cvs.Remove(subReg);
                        }
                    }
                }

                if (cvs.Count == 0)
                {
                    finished = true;
                }
            }
        }

        return res;
    }

    private static double BulgeFromCurve(Curve cv, bool clockwise)
    {
        double bulge = 0.0;
        Arc? a = cv as Arc;
        if (a != null)
        {
            double newStart = a.StartAngle > a.EndAngle ? a.StartAngle - (8 * Atan(1)) : a.StartAngle;
            // Угол начала обычно больше угла конца, так как дуги идут против часовой стрелки.
            // (Если это не так, значит дуга пересекает линию 0 градусов, и мы можем вычесть 2π из угла начала.)


            // Выпуклость определяется как тангенс
            // одной четверти входящего угла
            bulge = Tan((a.EndAngle - newStart) / 4);
            // Если кривая идёт по часовой стрелке, инвертируем выпуклость
            if (clockwise)
            {
                bulge = -bulge;
            }
        }

        return bulge;
    }

    public static IEnumerable<(HatchLoopTypes, Curve2dCollection, IntegerCollection)> GetLoops(this Region region)
    {
        Plane plane = new(Point3d.Origin, region.Normal);
        // Получаем представление границы области
        using Brep brep = new(region);
        foreach (var complex in brep.Complexes)
        {
            foreach (var loop in complex.Shells.First().Faces.First().Loops)
            {
                Curve2dCollection edgePtrCollection = [];
                IntegerCollection edgeTypeCollection = [];
                foreach (var edge in loop.Edges.Select(e => ((ExternalCurve3d)e.Curve).NativeCurve).ToOrderedArray())
                {
                    if (edge is CircularArc3d arc)
                    {
                        _ = edgePtrCollection.Add(new CircularArc2d(
                            arc.StartPoint.Convert2d(plane),
                            arc.EvaluatePoint((arc.GetParameterOf(arc.StartPoint) + arc.GetParameterOf(arc.EndPoint)) /
                                              2.0).Convert2d(plane),
                            arc.EndPoint.Convert2d(plane)));
                        _ = edgeTypeCollection.Add(2);
                    }
                    else if (edge is LineSegment3d line)
                    {
                        _ = edgePtrCollection.Add(new LineSegment2d(
                            line.StartPoint.Convert2d(plane),
                            line.EndPoint.Convert2d(plane)));
                        _ = edgeTypeCollection.Add(1);
                    }
                }

                if (loop.LoopType == LoopType.LoopExterior)
                {
                    yield return (HatchLoopTypes.External, edgePtrCollection, edgeTypeCollection);
                }
                else
                {
                    yield return (HatchLoopTypes.Default, edgePtrCollection, edgeTypeCollection);
                }
            }
        }
    }
}
