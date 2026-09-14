using Autodesk.AutoCAD.Colors;

namespace AutoBIMFusion.QuickPdf.Frames;

/// <summary>
///     Один раз создаёт слой рамок и классифицирует геометрию пространства модели.
///     Вызывающий код должен удерживать <see cref="Autodesk.AutoCAD.ApplicationServices.DocumentLock"/>.
/// </summary>
public static class FrameListInitializer
{
    private const string LayerName = "FRAMELIST";
    private const double CoordinateTolerance = 3;

    public static void EnsureInitialized(Database database)
    {
        using Transaction transaction = database.TransactionManager.StartTransaction();
        LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (layerTable.Has(LayerName))
        {
            return;
        }

        ObjectId frameLayerId = CreateLayer(layerTable, transaction);
        Dictionary<int, ObjectId> sourceObjects = [];
        IReadOnlyList<FramePath> paths = ReadModelPaths(database, transaction, sourceObjects);
        IReadOnlyList<DetectedFrame> frames = AxisAlignedFrameDetector.Find(paths, CoordinateTolerance);
        ObjectId[] selectedObjects = frames
            .SelectMany(frame => frame.SourceIds)
            .Distinct()
            .Select(sourceId => sourceObjects[sourceId])
            .ToArray();

        MoveToLayer(selectedObjects, frameLayerId, transaction);
        transaction.Commit();
    }

    private static ObjectId CreateLayer(LayerTable layerTable, Transaction transaction)
    {
        layerTable.UpgradeOpen();
        using LayerTableRecord layer = new()
        {
            Name = LayerName,
            Color = Color.FromColorIndex(ColorMethod.ByAci, 4),
            LineWeight = LineWeight.LineWeight050,
            IsPlottable = false
        };
        ObjectId layerId = layerTable.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
        return layerId;
    }

    private static IReadOnlyList<FramePath> ReadModelPaths(
        Database database,
        Transaction transaction,
        IDictionary<int, ObjectId> sourceObjects)
    {
        BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        List<FramePath> paths = [];
        foreach (ObjectId objectId in modelSpace)
        {
            Entity? entity = transaction.GetObject(objectId, OpenMode.ForRead, false) as Entity;
            if (entity is null || !TryReadPoints(entity, transaction, out bool closed, out FramePoint[] points))
            {
                continue;
            }

            int sourceId = paths.Count;
            paths.Add(new FramePath(sourceId, closed, points));
            sourceObjects.Add(sourceId, objectId);
        }

        return paths;
    }

    private static bool TryReadPoints(
        Entity entity,
        Transaction transaction,
        out bool closed,
        out FramePoint[] points)
    {
        switch (entity)
        {
            case Line line:
                closed = false;
                points = [ToFramePoint(line.StartPoint), ToFramePoint(line.EndPoint)];
                return SameElevation(line.StartPoint, line.EndPoint);

            case Polyline polyline when HasOnlyLineSegments(polyline):
                closed = polyline.Closed;
                points = Enumerable.Range(0, polyline.NumberOfVertices)
                    .Select(index => ToFramePoint(polyline.GetPoint3dAt(index)))
                    .ToArray();
                return HasConstantElevation(polyline.NumberOfVertices,
                    index => polyline.GetPoint3dAt(index));

            case Polyline2d polyline2d when polyline2d.PolyType == Poly2dType.SimplePoly:
                List<Vertex2d> vertices2d = polyline2d
                    .Cast<ObjectId>()
                    .Select(id => (Vertex2d)transaction.GetObject(id, OpenMode.ForRead))
                    .ToList();
                if (HasBulges(vertices2d, polyline2d.Closed))
                {
                    break;
                }

                closed = polyline2d.Closed;
                points = vertices2d.Select(vertex => ToFramePoint(vertex.Position)).ToArray();
                return HasConstantElevation(vertices2d.Select(vertex => vertex.Position));

            case Polyline3d polyline3d when polyline3d.PolyType == Poly3dType.SimplePoly:
                Point3d[] vertices3d = polyline3d
                    .Cast<ObjectId>()
                    .Select(id => ((PolylineVertex3d)transaction.GetObject(id, OpenMode.ForRead)).Position)
                    .ToArray();
                closed = polyline3d.Closed;
                points = vertices3d.Select(ToFramePoint).ToArray();
                return HasConstantElevation(vertices3d);
        }

        closed = false;
        points = [];
        return false;
    }

    private static bool HasOnlyLineSegments(Polyline polyline)
    {
        int segmentCount = polyline.Closed ? polyline.NumberOfVertices : polyline.NumberOfVertices - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            SegmentType segmentType = polyline.GetSegmentType(index);
            if (segmentType is not SegmentType.Line and not SegmentType.Coincident)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBulges(IReadOnlyList<Vertex2d> vertices, bool closed)
    {
        int segmentCount = closed ? vertices.Count : Max(0, vertices.Count - 1);
        for (int index = 0; index < segmentCount; index++)
        {
            if (Abs(vertices[index].Bulge) > 1e-9)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConstantElevation(int count, Func<int, Point3d> pointAt)
    {
        if (count == 0)
        {
            return false;
        }

        double elevation = pointAt(0).Z;
        for (int index = 1; index < count; index++)
        {
            if (Abs(pointAt(index).Z - elevation) > CoordinateTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasConstantElevation(IEnumerable<Point3d> points)
    {
        using IEnumerator<Point3d> enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        double elevation = enumerator.Current.Z;
        while (enumerator.MoveNext())
        {
            if (Abs(enumerator.Current.Z - elevation) > CoordinateTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameElevation(Point3d first, Point3d second)
    {
        return Abs(first.Z - second.Z) <= CoordinateTolerance;
    }

    private static FramePoint ToFramePoint(Point3d point)
    {
        return new FramePoint(point.X, point.Y);
    }

    private static void MoveToLayer(
        IEnumerable<ObjectId> objectIds,
        ObjectId targetLayerId,
        Transaction transaction)
    {
        Dictionary<ObjectId, LayerTableRecord> unlockedLayers = [];
        foreach (ObjectId objectId in objectIds)
        {
            Entity entity = (Entity)transaction.GetObject(objectId, OpenMode.ForRead);
            LayerTableRecord sourceLayer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForRead);
            if (sourceLayer.IsLocked && !unlockedLayers.ContainsKey(sourceLayer.ObjectId))
            {
                sourceLayer.UpgradeOpen();
                sourceLayer.IsLocked = false;
                unlockedLayers.Add(sourceLayer.ObjectId, sourceLayer);
            }

            entity.UpgradeOpen();
            entity.LayerId = targetLayerId;
            entity.ColorIndex = 256;
            entity.LineWeight = LineWeight.ByLayer;
        }

        foreach (LayerTableRecord layer in unlockedLayers.Values)
        {
            layer.IsLocked = true;
        }
    }
}
