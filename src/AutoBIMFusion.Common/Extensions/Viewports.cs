using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Viewport = Autodesk.AutoCAD.DatabaseServices.Viewport;

namespace SioForgeCAD.Commun.Extensions;

public static class ViewportsExtensions
{
    public static IntegerCollection GetViewPortsNumbers(this TransientManager _)
    {
        //https://www.keanw.com/2011/03/drawing-transient-graphics-appropriately-in-autocad-within-multiple-paperspace-viewports-using-net.html
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();
        // Are we in model space outside floating viewports?
        // Then we'll initalize an empty IntegerCollection

        if (db.TileMode) return new IntegerCollection();

        var vps = new List<int>();

        var trx = db.TransactionManager.StartTransaction();
        using (trx)
        {
            var vp = trx.GetObject(ed.ActiveViewportId, OpenMode.ForRead) as Viewport;

            // Are we in paper space and not inside a floating
            // viewport? Then only the paper space viewport itself
            // is of interest
            if (vp?.Number == 1)
                vps.Add(1);
            else
                // Now we're inside a floating viewport and
                // will display transients in active viewports
                foreach (ObjectId vpId in db.GetViewports(false))
                {
                    vp = (Viewport)trx.GetObject(vpId, OpenMode.ForRead);
                    vps.Add(vp.Number);
                }

            trx.Commit();
        }

        var ints = new int[vps.Count];
        vps.CopyTo(ints, 0);
        return ints.ToIntegerCollection();
    }

    public static Matrix3d GetModelToPaperTransform(this Viewport vport)
    {
        //https://www.theswamp.org/index.php?action=post;quote=477118;topic=42503.0;last_msg=596197
        var center = new Point3d(vport.ViewCenter.X, vport.ViewCenter.Y, 0.0);
        return Matrix3d.Displacement(new Vector3d(vport.CenterPoint.X - center.X, vport.CenterPoint.Y - center.Y, 0.0))
               * Matrix3d.Scaling(vport.CustomScale, center)
               * Matrix3d.Rotation(vport.TwistAngle, Vector3d.ZAxis, Point3d.Origin)
               * Matrix3d.WorldToPlane(new Plane(vport.ViewTarget, vport.ViewDirection));
    }

    public static Matrix3d GetPaperToModelTransform(this Viewport vport)
    {
        return vport.GetModelToPaperTransform().Inverse();
    }

    public static void PaperToModel(this Entity entity, Viewport vport)
    {
        entity.TransformBy(vport.GetModelToPaperTransform().Inverse());
    }

    public static void ModelToPaper(this Entity entity, Viewport viewport)
    {
        entity.TransformBy(viewport.GetModelToPaperTransform());
    }

    public static void PaperToModel(this IEnumerable<Entity> src, Viewport viewport)
    {
        var xform = viewport.GetModelToPaperTransform().Inverse();
        foreach (var ent in src) ent.TransformBy(xform);
    }

    public static void ModelToPaper(this IEnumerable<Entity> src, Viewport viewport)
    {
        var xform = viewport.GetModelToPaperTransform();
        foreach (var ent in src) ent.TransformBy(xform);
    }

    public static bool IsInModel(this Editor _)
    {
        return Generic.GetDatabase().TileMode;
    }

    public static bool IsInLayout(this Editor ed)
    {
        return !ed.IsInModel();
    }

    public static bool IsInLayoutPaper(this Editor ed)
    {
        var db = ed.Document.Database;

        if (db.TileMode ||
            db.PaperSpaceVportId == ObjectId.Null ||
            ed.CurrentViewportObjectId == ObjectId.Null)
            return false;

        if (ed.CurrentViewportObjectId == db.PaperSpaceVportId) return true;

        return false;
    }

    public static bool IsInLayoutViewport(this Editor ed)
    {
        return ed.IsInLayout() && !ed.IsInLayoutPaper();
    }


    public static List<ObjectId> GetAllViewportsInPaperSpace(this Editor _, BlockTableRecord btr)
    {
        var db = Generic.GetDatabase();

        var ListOfViewPorts = new List<ObjectId>();

        foreach (var objId in btr)
        {
            var entity = objId.GetEntity();
            if (entity != null && entity is Viewport && db.GetViewports(false).Contains(entity.ObjectId))
                ListOfViewPorts.Add(entity.ObjectId);
        }

        return ListOfViewPorts;
    }

    public static Polyline GetBoundary(this Viewport viewport)
    {
        var db = Generic.GetDatabase();

        using (var trx = db.TransactionManager.StartTransaction())
        {
            try
            {
                if (viewport == null) return null;
                if (viewport.NonRectClipEntityId != ObjectId.Null)
                {
                    // Get the non-rectangular clipping boundary
                    var clipEntity = viewport.NonRectClipEntityId.GetEntity();
                    if (clipEntity is Curve clipEntCurve) return clipEntCurve.ToPolyline();
                    return null;
                }
                else
                {
                    // Get the standard rectangular boundary
                    var center = viewport.CenterPoint;
                    var width = viewport.Width;
                    var height = viewport.Height;

                    var lowerLeft = new Point3d(center.X - width / 2, center.Y - height / 2, center.Z);
                    var lowerRight = new Point3d(center.X + width / 2, center.Y - height / 2, center.Z);
                    var upperRight = new Point3d(center.X + width / 2, center.Y + height / 2, center.Z);
                    var upperLeft = new Point3d(center.X - width / 2, center.Y + height / 2, center.Z);

                    var polyline = new Polyline();
                    polyline.AddVertexAt(0, lowerLeft.ToPoint2d(), 0, 0, 0);
                    polyline.AddVertexAt(1, lowerRight.ToPoint2d(), 0, 0, 0);
                    polyline.AddVertexAt(2, upperRight.ToPoint2d(), 0, 0, 0);
                    polyline.AddVertexAt(3, upperLeft.ToPoint2d(), 0, 0, 0);
                    polyline.Closed = true;
                    return polyline;
                }
            }
            finally
            {
                trx.Commit();
            }
        }
    }
}
