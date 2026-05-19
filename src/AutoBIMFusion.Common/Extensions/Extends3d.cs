using AutoBIMFusion.Common.Mist;

namespace AutoBIMFusion.Common.Extensions;

public static class Extends3dExtensions
{
    private static readonly object _GetExtentsLock = new();

    public static double Left(this Extents3d extends)
    {
        return extends.MinPoint.X;
    }

    public static double Right(this Extents3d extends)
    {
        return extends.MaxPoint.X;
    }

    public static double Top(this Extents3d extends)
    {
        return extends.MaxPoint.Y;
    }

    public static double Bottom(this Extents3d extends)
    {
        return extends.MinPoint.Y;
    }

    public static Point3d Middle(this Extents3d extends)
    {
        return new Point3d((extends.Left() + extends.Right()) / 2, (extends.Top() + extends.Bottom()) / 2, 0);
    }

    public static Point3d TopLeft(this Extents3d extends)
    {
        return new Point3d(extends.Left(), extends.Top(), 0);
    }

    public static Point3d TopRight(this Extents3d extends)
    {
        return new Point3d(extends.MaxPoint.X, extends.Top(), 0);
    }

    public static Point3d BottomLeft(this Extents3d extends)
    {
        return new Point3d(extends.Left(), extends.Bottom(), 0);
    }

    public static Point3d BottomRight(this Extents3d extends)
    {
        return new Point3d(extends.Right(), extends.Bottom(), 0);
    }

    public static ExtentsSize Size(this Extents3d extends)
    {
        return new ExtentsSize(
            extends.TopLeft().DistanceTo(extends.TopRight()),
            extends.TopLeft().DistanceTo(extends.BottomLeft()));
    }

    public static bool CollideWith(this Extents3d a, Extents3d b)
    {
        return !(b.Left() > a.Right() || b.Right() < a.Left() || b.Top() < a.Bottom() || b.Bottom() > a.Top());
    }

    public static bool CollideWithOrConnected(this Extents3d a, Extents3d b)
    {
        return !(b.Left() >= a.Right() || b.Right() <= a.Left() || b.Top() <= a.Bottom() || b.Bottom() >= a.Top());
    }

    public static bool IsFullyInside(this Extents3d a, Extents3d b)
    {
        return a.MinPoint.X >= b.MinPoint.X &&
               a.MaxPoint.X <= b.MaxPoint.X &&
               a.MinPoint.Y >= b.MinPoint.Y &&
               a.MaxPoint.Y <= b.MaxPoint.Y;
    }

    public static Extents3d GetExtents(this Entity entity)
    {
        //GetExtents is not thread safe
        lock (_GetExtentsLock)
        {
            return entity != null && entity?.Bounds.HasValue == true ? entity.GeometricExtents : new Extents3d();
        }
    }

    public static Extents3d GetExtents(this IEnumerable<Extents3d> entities)
    {
        if (entities.Any())
        {
            Extents3d extent = new();
            foreach (var dbobj in entities) extent.AddExtents(dbobj);

            return extent;
        }

        return new Extents3d();
    }

    public static Extents3d GetExtents(this IEnumerable<object> entities)
    {
        if (entities.Any())
        {
            Extents3d extent = new();
            foreach (var dbobj in entities)
                if (dbobj is Entity ent)
                    extent.AddExtents(ent.GetExtents());

            return extent;
        }

        return new Extents3d();
    }

    public static Extents3d GetExtents(this DBObjectCollection entities)
    {
        return entities.ToArray().GetExtents();
    }

    public static Extents3d GetExtents(this IEnumerable<ObjectId> entities)
    {
        List<Entity> list = [];
        foreach (var ent in entities)
            if (ent.GetEntity() is Entity entity)
                list.Add(entity);

        return list.GetExtents();
    }

    public static Extents3d GetVisualExtents(this Entity ent, out Point3dCollection entPts)
    {
        var db = Generic.GetDatabase();

        using var trx = db.TransactionManager.StartTransaction();
        var btr = (BlockTableRecord)trx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        entPts = CollectPoints(trx, ent);

        Extents3d extents = new();
        foreach (Point3d item in entPts) extents.AddExtents(new Extents3d(item, item));

        trx.Commit();
        return extents;
    }

    private static Point3dCollection CollectPoints(Transaction trx, Entity ent)
    {
        // The collection of points to populate and return
        Point3dCollection pts = [];

        // We'll start by checking a block reference for
        // attributes, getting their bounds and adding
        // them to the point list. We'll still explode
        // the BlockReference later, to gather points
        // from other geometry, it's just that approach
        // doesn't work for attributes (we only get the
        // AttributeDefinitions, which don't have bounds)

        var br = ent as BlockReference;
        if (br != null)
            foreach (ObjectId arId in br.AttributeCollection)
            {
                var obj = trx.GetObject(arId, OpenMode.ForRead);
                if (obj is AttributeReference ar) ar.ExtractBounds(pts);
            }
        // If we have a curve - other than a polyline, which
        // we will want to explode - we'll get points along
        // its length

        var cur = ent as Curve;

        if (cur is not null and not (Polyline or Polyline2d or Polyline3d))
        {
            // Two points are enough for a line, we'll go with
            // a higher number for other curves
            var segs = ent is Line ? 2 : 20;
            var param = cur.EndParam - cur.StartParam;

            for (var i = 0; i < segs; i++)
                try
                {
                    var pt = cur.GetPointAtParameter(cur.StartParam + i * param / (segs - 1));
                    _ = pts.Add(pt);
                }
                catch
                {
                }
        }
        else if (ent is DBPoint dBPoint)
        {
            _ = pts.Add(dBPoint.Position);
        }
        else if (ent is DBText dBText)
        {
            dBText.ExtractBounds(pts);
        }
        else if (ent is Face f)
        {
            try
            {
                for (short i = 0; i < 4; i++) _ = pts.Add(f.GetVertexAt(i));
            }
            catch
            {
            }
        }
        else if (ent is Solid sol)
        {
            try
            {
                for (short i = 0; i < 4; i++) _ = pts.Add(sol.GetPointAt(i));
            }
            catch
            {
            }
        }
        else
        {
            // Here's where we attempt to explode other types
            // of object
            DBObjectCollection oc = [];
            try
            {
                ent.Explode(oc);
                if (oc.Count > 0)
                    foreach (DBObject obj in oc)
                    {
                        var ent2 = obj as Entity;
                        if (ent2?.Visible == true)
                            foreach (Point3d pt in CollectPoints(trx, ent2))
                                _ = pts.Add(pt);

                        obj.Dispose();
                    }
            }
            catch
            {
            }
        }

        return pts;
    }

    public static List<Extents3d> GetExplodedExtents(this Entity ent)
    {
        List<Extents3d> ext;
        var db = Generic.GetDatabase();

        using var trx = db.TransactionManager.StartTransaction();
        var btr = (BlockTableRecord)trx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        ext = CollectExtends(trx, ent);

        trx.Commit();
        return ext;
    }

    private static List<Extents3d> CollectExtends(Transaction trx, Entity ent)
    {
        List<Extents3d> ext = [];

        if (ent is DBPoint dBPoint)
        {
            ext.Add(dBPoint.GetExtents());
        }
        else if (ent is Entity and (Line or
                 Circle or
                 Arc or
                 Hatch or
                 Line)
                )
        {
            ext.Add(ent.GetExtents());
        }
        else
        {
            // Here's where we attempt to explode other types
            // of object
            DBObjectCollection oc = [];
            try
            {
                ent.Explode(oc);
                if (oc.Count > 0)
                    foreach (DBObject obj in oc)
                    {
                        var ent2 = obj as Entity;
                        if (ent2?.Visible == true) ext.AddRange(CollectExtends(trx, ent2));

                        obj.Dispose();
                    }
                else
                    ext.Add(ent.GetExtents());
            }
            catch
            {
                ext.Add(ent.GetExtents());
            }
        }

        return ext;
    }

    public static Point3d GetCenter(this Extents3d extents)
    {
        return new Point3d(
            (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
            (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
            (extents.MinPoint.Z + extents.MaxPoint.Z) / 2.0
        );
    }

    public static void Expand(this ref Extents3d extents, double factor)
    {
        var center = extents.GetCenter();
        var Min = center + factor * (extents.MinPoint - center);
        var Max = center + factor * (extents.MaxPoint - center);
        try
        {
            extents = new Extents3d(Min, Max);
        }
        catch
        {
            extents = new Extents3d();
        }
    }

    public static bool IsPointIn(this Extents3d extents, Point3d point)
    {
        return point.X >= extents.MinPoint.X && point.X <= extents.MaxPoint.X
                                             && point.Y >= extents.MinPoint.Y && point.Y <= extents.MaxPoint.Y
                                             && point.Z >= extents.MinPoint.Z && point.Z <= extents.MaxPoint.Z;
    }

    public static bool IsInside(this Polyline LineB, Extents3d extents, bool CheckEach = true)
    {
        var NumberOfVertices = 1;
        if (CheckEach) NumberOfVertices = LineB.GetReelNumberOfVertices();

        for (var PolylineSegmentIndex = 0; PolylineSegmentIndex < NumberOfVertices; PolylineSegmentIndex++)
        {
            var (StartPoint, EndPoint, _) = LineB.GetSegmentAt(PolylineSegmentIndex);
            Point3d MiddlePoint;
            if (LineB.GetSegmentType(PolylineSegmentIndex) == SegmentType.Arc)
            {
                var Startparam = LineB.GetParameterAtPoint(StartPoint);
                var Endparam = LineB.GetParameterAtPoint(EndPoint);
                MiddlePoint = LineB.GetPointAtParam(Startparam + (Endparam - Startparam) / 2);
            }
            else
            {
                MiddlePoint = StartPoint.GetMiddlePoint(EndPoint);
            }

            if (StartPoint.DistanceTo(EndPoint) / 2 >
                Generic.MediumTolerance.EqualPoint)
                if (!extents.IsPointIn(MiddlePoint))
                    return false;
        }

        return true;
    }

    public static Point3d GetCenter(this IEnumerable<ObjectId> entIds)
    {
        return entIds.GetExtents().GetCenter();
    }

    public static Polyline GetGeometry(this Extents3d extents3D)
    {
        Polyline outline = new();
        outline.AddVertex(extents3D.TopLeft());
        outline.AddVertex(extents3D.TopRight());
        outline.AddVertex(extents3D.BottomRight());
        outline.AddVertex(extents3D.BottomLeft());
        outline.Closed = true;
        return outline;
    }

    public static Rectangle3d ToRectangle3d(this Extents3d extents3D)
    {
        return new Rectangle3d(extents3D.TopLeft(), extents3D.TopRight(), extents3D.BottomLeft(),
            extents3D.BottomRight());
    }

    // Lifted from
    // http://docs.autodesk.com/ACD/2010/ENU/AutoCAD%20.NET%20Developer%27s%20Guide/files/WS1a9193826455f5ff2566ffd511ff6f8c7ca-4363.htm
    public static void ZoomExtents(this Extents3d extents)
    {
        var ed = Generic.GetEditor();
        // Get the current view
        using var acView = ed.GetCurrentView();
        // Translate WCS coordinates to DCS
        var matWCS2DCS = Matrix3d.Rotation(-acView.ViewTwist, acView.ViewDirection, acView.Target) *
                         Matrix3d.Displacement(acView.Target - Point3d.Origin) *
                         Matrix3d.PlaneToWorld(acView.ViewDirection);

        // Calculate the ratio between the width and height of the current view
        var dViewRatio = acView.Width / acView.Height;

        // Tranform the extents of the view
        extents.TransformBy(matWCS2DCS.Inverse());

        // Calculate the new width and height of the current view
        var dWidth = extents.MaxPoint.X - extents.MinPoint.X;
        var dHeight = extents.MaxPoint.Y - extents.MinPoint.Y;

        // Check to see if the new width fits in current window
        if (dWidth > dHeight * dViewRatio) dHeight = dWidth / dViewRatio;

        // Get the center of the view
        Point2d pNewCentPt = new((extents.MaxPoint.X + extents.MinPoint.X) * 0.5,
            (extents.MaxPoint.Y + extents.MinPoint.Y) * 0.5);

        // Resize the view
        acView.Height = dHeight;
        acView.Width = dWidth;

        // Set the center of the view
        acView.CenterPoint = pNewCentPt;

        // Set the current view
        ed.SetCurrentView(acView);
    }
}

public readonly record struct ExtentsSize(double Width, double Height);
