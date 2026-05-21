using AutoBIMFusion.Common.AcadSupport;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Viewport = Autodesk.AutoCAD.DatabaseServices.Viewport;

namespace AutoBIMFusion.Common.Extensions;

public static class ViewportsExtensions
{
    public static IntegerCollection GetViewPortsNumbers(this TransientManager _)
    {
        //https://www.keanw.com/2011/03/drawing-transient-graphics-appropriately-in-autocad-within-multiple-paperspace-viewports-using-net.html
        Database db = AcadContext.GetDatabase();
        Editor ed = AcadContext.GetEditor();
        // Находимся в пространстве модели вне плавающих видовых экранов?
        // Тогда инициализируем пустую IntegerCollection

        if (db.TileMode)
        {
            return [];
        }

        List<int> vps = [];

        Transaction trx = db.TransactionManager.StartTransaction();
        using (trx)
        {
            Viewport? vp = trx.GetObject(ed.ActiveViewportId, OpenMode.ForRead) as Viewport;

            // Находимся в пространстве листа и не внутри плавающего
            // видового экрана? Тогда интересует только сам видовой экран листа
            if (vp?.Number == 1)
            {
                vps.Add(1);
            }
            else
            {
                // Теперь мы внутри плавающего видового экрана и
                // будем отображать временную графику в активных видовых экранах
                foreach (ObjectId vpId in db.GetViewports(false))
                {
                    vp = (Viewport)trx.GetObject(vpId, OpenMode.ForRead);
                    vps.Add(vp.Number);
                }
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
        Point3d center = new(vport.ViewCenter.X, vport.ViewCenter.Y, 0.0);
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
        entity.TransformBy(viewport.GetPaperToModelTransform());
    }

    public static void PaperToModel(this IEnumerable<Entity> src, Viewport viewport)
    {
        Matrix3d xform = viewport.GetModelToPaperTransform().Inverse();
        foreach (Entity ent in src)
        {
            ent.TransformBy(xform);
        }
    }

    public static void ModelToPaper(this IEnumerable<Entity> src, Viewport viewport)
    {
        Matrix3d xform = viewport.GetModelToPaperTransform();
        foreach (Entity ent in src)
        {
            ent.TransformBy(xform);
        }
    }

    public static bool IsInModel(this Editor _)
    {
        return AcadContext.GetDatabase().TileMode;
    }

    public static bool IsInLayout(this Editor ed)
    {
        return !ed.IsInModel();
    }

    public static bool IsInLayoutPaper(this Editor ed)
    {
        Database db = ed.Document.Database;

        return !db.TileMode &&
               db.PaperSpaceVportId != ObjectId.Null &&
               ed.CurrentViewportObjectId != ObjectId.Null && ed.CurrentViewportObjectId == db.PaperSpaceVportId;
    }

    public static bool IsInLayoutViewport(this Editor ed)
    {
        return ed.IsInLayout() && !ed.IsInLayoutPaper();
    }


    public static List<ObjectId> GetAllViewportsInPaperSpace(this Editor _, BlockTableRecord btr)
    {
        Database db = AcadContext.GetDatabase();

        List<ObjectId> ListOfViewPorts = [];

        foreach (ObjectId objId in btr)
        {
            Entity entity = objId.GetEntity();
            if (entity != null && entity is Viewport && db.GetViewports(false).Contains(entity.ObjectId))
            {
                ListOfViewPorts.Add(entity.ObjectId);
            }
        }

        return ListOfViewPorts;
    }

    public static Polyline GetBoundary(this Viewport viewport)
    {
        Database db = AcadContext.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        try
        {
            if (viewport == null)
            {
                return null;
            }

            if (viewport.NonRectClipEntityId != ObjectId.Null)
            {
                // Получаем нестандартную границу обрезки
                Entity clipEntity = viewport.NonRectClipEntityId.GetEntity();
                return clipEntity is Curve clipEntCurve ? clipEntCurve.ToPolyline() : null;
            }
            else
            {
                // Получаем стандартную прямоугольную границу
                Point3d center = viewport.CenterPoint;
                var width = viewport.Width;
                var height = viewport.Height;

                Point3d lowerLeft = new(center.X - (width / 2), center.Y - (height / 2), center.Z);
                Point3d lowerRight = new(center.X + (width / 2), center.Y - (height / 2), center.Z);
                Point3d upperRight = new(center.X + (width / 2), center.Y + (height / 2), center.Z);
                Point3d upperLeft = new(center.X - (width / 2), center.Y + (height / 2), center.Z);

                Polyline polyline = new();
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

    // --- DCS → WCS координатные преобразования для Viewport ---

    /// <summary>
    ///     Строит матрицу преобразования DCS → WCS для Viewport.
    ///     DCS (Display Coordinate System) — система координат экрана Viewport.
    /// </summary>
    public static Matrix3d GetDcsToWcsMatrix(this Viewport vp)
    {
        return Matrix3d.Displacement(vp.ViewTarget.GetAsVector()) *
               Matrix3d.PlaneToWorld(vp.ViewDirection) *
               Matrix3d.Rotation(vp.TwistAngle, Vector3d.ZAxis, Point3d.Origin);
    }

    /// <summary>
    ///     Возвращает ViewCenter в WCS.
    ///     ViewCenter — 2D-точка в DCS; для получения WCS применяется трансформация DCS → WCS.
    /// </summary>
    public static Point3d GetViewCenterWcs(this Viewport vp)
    {
        Matrix3d mat = vp.GetDcsToWcsMatrix();
        return new Point3d(vp.ViewCenter.X, vp.ViewCenter.Y, 0).TransformBy(mat);
    }

    /// <summary>
    ///     Разрешает эффективный масштаб Viewport (CustomScale > 0 или вычисленный из ViewHeight).
    /// </summary>
    public static double ResolveCustomScale(this Viewport vp)
    {
        return vp.CustomScale > 0 ? vp.CustomScale : vp.Height / Max(vp.ViewHeight, 1e-9);
    }

    /// <summary>
    ///     Вычисляет AABB в Model Space, видимый через Viewport.
    ///     Учитывает аспект, ViewHeight, ViewCenter и поворот (TwistAngle + ViewDirection).
    /// </summary>
    public static Extents3d ComputeModelWindow(this Viewport vp)
    {
        var aspectRatio = vp.Width / Max(vp.Height, 1e-9);
        var widthModel = vp.ViewHeight * aspectRatio;
        var heightModel = vp.ViewHeight;

        var halfW = widthModel / 2.0;
        var halfH = heightModel / 2.0;

        Matrix3d dcsToWcs = vp.GetDcsToWcsMatrix();
        Point2d vc = vp.ViewCenter;

        Point3d[] corners =
        [
            new Point3d(vc.X - halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X - halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs)
        ];

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;

        foreach (Point3d p in corners)
        {
            minX = Min(minX, p.X);
            maxX = Max(maxX, p.X);
            minY = Min(minY, p.Y);
            maxY = Max(maxY, p.Y);
            minZ = Min(minZ, p.Z);
            maxZ = Max(maxZ, p.Z);
        }

        return new Extents3d(new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
    }
}
