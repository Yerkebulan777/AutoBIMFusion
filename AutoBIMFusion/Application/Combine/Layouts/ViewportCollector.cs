namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Перечисляет активные Viewport'ы указанного листа и собирает LayoutViewportInfo.
/// Служебный viewport с Number == 1 исключается.
/// </summary>
internal static class ViewportCollector
{
    internal static List<ViewportInfo> Collect(Database db, string layoutName)
    {
        List<ViewportInfo> result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return result;
        }

        ObjectId layoutId = layoutDict.GetAt(layoutName);
        Layout layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            Viewport vp = (Viewport)trx.GetObject(id, OpenMode.ForRead);

            if (vp.Number == 1 || !vp.On)
            {
                continue;
            }

            double scale = ResolveScale(vp);

            if (scale <= 0)
            {
                continue;
            }

            Extents3d window = ComputeModelWindow(vp);
            Point3d viewCenterWcs = GetViewCenterWcs(vp);

            ViewportInfo info = new(
                VpId: id,
                Number: vp.Number,
                CenterPaper: vp.CenterPoint,
                WidthPaper: vp.Width,
                HeightPaper: vp.Height,
                ViewCenter: viewCenterWcs,
                ViewHeight: vp.ViewHeight,
                ViewTwist: vp.TwistAngle,
                CustomScale: scale,
                ModelWindow: window);
            result.Add(info);
        }

        trx.Commit();
        return result;
    }

    private static Point3d GetViewCenterWcs(Viewport vp)
    {
        // ViewCenter — это 2D-точка в DCS (Display Coordinate System).
        // Чтобы получить WCS, нужно применить трансформацию DCS -> WCS.
        Matrix3d mat = GetDcsToWcsMatrix(vp);
        return new Point3d(vp.ViewCenter.X, vp.ViewCenter.Y, 0).TransformBy(mat);
    }

    private static Matrix3d GetDcsToWcsMatrix(Viewport vp)
    {
        // DCS -> WCS для Viewport:
        // 1. Поворот на TwistAngle вокруг Z в Eye-координатах.
        // 2. Переход из Eye (Plane) в World через ViewDirection.
        // 3. Смещение на ViewTarget.
        return Matrix3d.Displacement(vp.ViewTarget.GetAsVector()) *
               Matrix3d.PlaneToWorld(vp.ViewDirection) *
               Matrix3d.Rotation(vp.TwistAngle, Vector3d.ZAxis, Point3d.Origin);
    }

    private static double ResolveScale(Viewport vp)
    {
        return vp.CustomScale > 0 ? vp.CustomScale : vp.Height / Max(vp.ViewHeight, 1e-9);
    }

    private static Extents3d ComputeModelWindow(Viewport vp)
    {
        double aspectRatio = vp.Width / Max(vp.Height, 1e-9);
        double widthModel = vp.ViewHeight * aspectRatio;
        double heightModel = vp.ViewHeight;

        double halfW = widthModel / 2.0;
        double halfH = heightModel / 2.0;

        Matrix3d dcsToWcs = GetDcsToWcsMatrix(vp);
        Point2d vc = vp.ViewCenter;

        Point3d[] corners =
        [
            new Point3d(vc.X - halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X - halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs),
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
