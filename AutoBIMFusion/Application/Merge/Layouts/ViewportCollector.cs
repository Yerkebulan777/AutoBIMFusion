namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Перечисляет активные Viewport'ы указанного листа и собирает LayoutViewportInfo.
/// Служебный viewport с Number == 1 исключается.
/// </summary>
internal static class ViewportCollector
{
    internal static List<LayoutViewportInfo> Collect(Database db, string layoutName)
    {
        List<LayoutViewportInfo> result = [];

        using Transaction tr = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            tr.Commit();
            return result;
        }

        ObjectId layoutId = layoutDict.GetAt(layoutName);
        Layout layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            Viewport vp = (Viewport)tr.GetObject(id, OpenMode.ForRead);

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
            Point3d viewCenter = new(vp.ViewCenter.X, vp.ViewCenter.Y, 0);
            LayoutViewportInfo info = new(
                VpId: id,
                Number: vp.Number,
                CenterPaper: vp.CenterPoint,
                WidthPaper: vp.Width,
                HeightPaper: vp.Height,
                ViewCenter: viewCenter,
                ViewHeight: vp.ViewHeight,
                ViewTwist: vp.TwistAngle,
                CustomScale: scale,
                ModelWindow: window);
            result.Add(info);
        }

        tr.Commit();
        return result;
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

        Point2d vc = vp.ViewCenter;
        double cs = Cos(vp.TwistAngle);
        double sn = Sin(vp.TwistAngle);

        Point2d[] corners =
        [
            new(-halfW, -halfH),
            new(+halfW, -halfH),
            new(+halfW, +halfH),
            new(-halfW, +halfH),
        ];

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (Point2d c in corners)
        {
            double x = vc.X + (c.X * cs) - (c.Y * sn);
            double y = vc.Y + (c.X * sn) + (c.Y * cs);

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        Point3d minPt = new(minX, minY, 0);
        Point3d maxPt = new(maxX, maxY, 0);
        return new Extents3d(minPt, maxPt);
    }
}
