namespace AutoBIMFusion.Merge.Combine.Layouts;

internal readonly record struct ViewportAabb(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ)
{
    internal static ViewportAabb FromXy(double minX, double minY, double maxX, double maxY)
        => new(minX, minY, 0, maxX, maxY, 0);

    internal double Diagonal
    {
        get
        {
            double dx = MaxX - MinX;
            double dy = MaxY - MinY;
            double dz = MaxZ - MinZ;
            return Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    internal bool IntersectsXy(ViewportAabb other)
        => MinX <= other.MaxX
           && MaxX >= other.MinX
           && MinY <= other.MaxY
           && MaxY >= other.MinY;

    internal bool ContainsXy(ViewportAabb inner, double tolerance = 1e-6)
        => inner.MinX >= MinX - tolerance
           && inner.MaxX <= MaxX + tolerance
           && inner.MinY >= MinY - tolerance
           && inner.MaxY <= MaxY + tolerance;
}

internal static class ViewportEntityClassifier
{
    internal const double HugeEntityDiagonalRatio = 3.0;
    internal const double SmallEntityDiagonalThreshold = 100.0;

    internal static ModelEntitySelection Classify(
        ViewportAabb window,
        ViewportAabb mainWindow,
        ViewportAabb entity)
    {
        if (!window.IntersectsXy(entity))
        {
            return ModelEntitySelection.OutsideWindow;
        }

        double entityDiagonal = entity.Diagonal;

        if (entityDiagonal <= SmallEntityDiagonalThreshold && !window.ContainsXy(entity))
        {
            return ModelEntitySelection.SmallPartialOutsideWindow;
        }

        double windowDiagonal = window.Diagonal;

        if (windowDiagonal > 0
            && entityDiagonal > windowDiagonal * HugeEntityDiagonalRatio
            && mainWindow.IntersectsXy(entity))
        {
            return ModelEntitySelection.HugeInMainWindow;
        }

        return ModelEntitySelection.Selected;
    }
}

internal enum ModelEntitySelection
{
    Selected,
    OutsideWindow,
    SmallPartialOutsideWindow,
    HugeInMainWindow
}
