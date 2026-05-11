using SioForgeCAD.Commun.Drawing;

namespace SioForgeCAD.Commun.Extensions;

public static class Vector3dExtensions
{
    public static ObjectId DrawVector(this Vector3d vector3d, Point3d startPoint, int ColorIndex = 0)
    {
        var vectorEndPoint = startPoint.Add(vector3d);
        var vectorLine = new Line(startPoint, vectorEndPoint)
        {
            ColorIndex = ColorIndex
        };
        return vectorLine.AddToDrawing();
    }

    public static Vector3d SetLength(this Vector3d vector3d, double Length)
    {
        return vector3d.GetNormal().MultiplyBy(Length);
    }

    public static bool IsVectorOnRightSide(this Vector3d vectorToCheckSide, Vector3d referenceVector)
    {
        // Normaliser les vecteurs
        vectorToCheckSide = vectorToCheckSide.GetNormal();
        referenceVector = referenceVector.GetNormal();
        var crossProduct = vectorToCheckSide.CrossProduct(referenceVector);
        // Vérifier la composante Z du produit vectoriel pour déterminer l'orientation
        return crossProduct.Z >= 0;
    }

    public static Vector3d Inverse(this Vector3d vector3D)
    {
        return vector3D.MultiplyBy(-1);
    }

    public static Vector2d Inverse(this Vector2d vector2D)
    {
        return vector2D.MultiplyBy(-1);
    }

    public static double GetRotationRelativeToSCG(this Vector3d vector)
    {
        var xAxisWCS = new Vector2d(0, 1);
        var dot = DotProduct(vector.ToVector2d(),
            xAxisWCS); //vector.X * xAxisWCS.X + vector.Y * xAxisWCS.Y;      // Dot product between [x1, y1] and [x2, y2]
        var det = vector.ToVector2d().CrossProduct(xAxisWCS); //vector.X * xAxisWCS.Y - vector.Y * xAxisWCS.X;     
        var angle = Atan2(det, dot);
        var angleDegrees = angle * (180.0 / PI);
        return angleDegrees < 0 ? 360.0 + angleDegrees : angleDegrees;
    }

    public static Vector2d ToVector2d(this Vector3d vector)
    {
        return new Vector2d(vector.X, vector.Y);
    }

    public static Vector3d ToVector3d(this Vector2d vector)
    {
        return new Vector3d(vector.X, vector.Y, 0);
    }

    /// <summary>
    ///     Gets the dot produc of two Vector2ds.
    /// </summary>
    /// <param name="v1">The vector 1.</param>
    /// <param name="v2">The vector 2.</param>
    /// <returns>The dot product.</returns>
    public static double DotProduct(this Vector2d v1, Vector2d v2)
    {
        return v1.X * v2.X + v1.Y * v2.Y;
    }

    /// <summary>
    ///     Gets the cross produc of two Vector2ds.
    /// </summary>
    /// <param name="v1">The vector 1.</param>
    /// <param name="v2">The vector 2.</param>
    /// <returns>The cross product.</returns>
    public static double CrossProduct(this Vector2d v1, Vector2d v2)
    {
        return v1.X * v2.Y - v1.Y * v2.X;
    }

    public static bool IsColinear(this Vector3d v1, Vector3d v2, Tolerance tol)
    {
        return v1.ToVector2d().IsColinear(v2.ToVector2d(), tol);
    }

    public static bool IsColinear(this Vector2d v1, Vector2d v2, Tolerance tol)
    {
        return Abs(v1.CrossProduct(v2)) < tol.EqualPoint;
    }

    public static Point3d FindProjectedIntersection(this Vector3d FirstVector, Point3d FirstVectorBasePoint,
        Vector3d SecondVector, Point3d SecondVectorBasePoint)
    {
        var deltaStartPoints = FirstVectorBasePoint - SecondVectorBasePoint;
        var a = FirstVector.DotProduct(FirstVector);
        var b = FirstVector.DotProduct(SecondVector);
        var c = SecondVector.DotProduct(SecondVector);
        var d = FirstVector.DotProduct(deltaStartPoints);
        var e = SecondVector.DotProduct(deltaStartPoints);
        var s = (a * e - b * d) / (a * c - b * b);
        return SecondVectorBasePoint + s * SecondVector;
    }
}
