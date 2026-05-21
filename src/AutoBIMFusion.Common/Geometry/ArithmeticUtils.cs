using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Geometry;

public static class ArithmeticUtils
{
    public static (double Altitude, double Slope) ComputeSlopeAndIntermediate(CotePoints First, CotePoints Second,
        Points Intermediaire)
    {
        if (First is null || Second is null)
        {
            return (0, 0);
        }

        Point3d FirstPointSCG = First.Points.SCG;
        Point3d SecondPointSCG = Second.Points.SCG;
        Point3d IntermediairePointInSCG = Intermediaire.SCG;

        //calcul distance :
        double AI_dist_horizontal = Pow(IntermediairePointInSCG.X - FirstPointSCG.X, 2);
        double AI_dist_vertical = Pow(IntermediairePointInSCG.Y - FirstPointSCG.Y, 2);
        double AI_dist_total = Sqrt(AI_dist_horizontal + AI_dist_vertical);

        double IB_dist_horizontal = Pow(SecondPointSCG.X - IntermediairePointInSCG.X, 2);
        double IB_dist_vertical = Pow(SecondPointSCG.Y - IntermediairePointInSCG.Y, 2);
        double IB_dist_total = Sqrt(IB_dist_horizontal + IB_dist_vertical);

        double AB_dist_horizontal = Pow(SecondPointSCG.X - FirstPointSCG.X, 2);
        double AB_dist_vertical = Pow(SecondPointSCG.Y - FirstPointSCG.Y, 2);
        double AB_dist_total = Sqrt(AB_dist_horizontal + AB_dist_vertical);

        double AIB_dist_total = AI_dist_total + IB_dist_total;

        double AI_pourcent = AI_dist_total / AIB_dist_total;
        double AB_cote_dif = Abs(First.Altitude - Second.Altitude);
        double I_dif_to_add_sus = AB_cote_dif * AI_pourcent;
        double I_cote = First.Altitude;

        double pente = Round(AB_cote_dif / AB_dist_total * 100.00, 2);

        if (First.Altitude > Second.Altitude)
        {
            I_cote -= I_dif_to_add_sus;
        }
        else
        {
            I_cote += I_dif_to_add_sus;
        }

        if (double.IsNaN(I_cote))
        {
            I_cote = First.Altitude;
        }

        if (double.IsNaN(pente))
        {
            pente = 0;
        }

        return (I_cote, pente);
    }

    public static double ComputePointFromSlopePourcentage(double OriginAltitude, double DistanceFromOrigin,
        double Slope)
    {
        const double PourcentageToDecimalRatio = 0.01;
        double Altimetrie = Abs(OriginAltitude) + (Slope * PourcentageToDecimalRatio * Abs(DistanceFromOrigin));
        return Altimetrie;
    }

    public static CotePoints FindDistanceToAltitudeBetweenTwoPoint(CotePoints First, CotePoints Second,
        double SearchedAltitude)
    {
        CotePoints LowestCotePoints;
        CotePoints UpperCotePoints;
        if (First.Altitude < Second.Altitude)
        {
            LowestCotePoints = First;
            UpperCotePoints = Second;
        }
        else
        {
            LowestCotePoints = Second;
            UpperCotePoints = First;
        }

        double LUDistance = LowestCotePoints.Points.SCG.DistanceTo(UpperCotePoints.Points.SCG);

        double LUDiffAltitude = UpperCotePoints.Altitude - LowestCotePoints.Altitude;
        double LIDiffAltitude = SearchedAltitude - LowestCotePoints.Altitude;
        double DistRatio = LIDiffAltitude / LUDiffAltitude;
        double DistFromLowest = DistRatio * LUDistance;


        //var DistanceLI = LowestCotePoints.Altitude - SearchedAltitude;
        //if (DistanceLI == 0 || DistanceFS == 0) { return CotePoints.Null; }
        //var DistRatio = DistanceLI / DistanceFS;
        //var DistFromLowest = DistanceFS * DistRatio;
        Vector3d VectorLI = LowestCotePoints.Points.SCG.GetVectorTo(UpperCotePoints.Points.SCG).SetLength(DistFromLowest);
        return new CotePoints(
            Points.From3DPoint(LowestCotePoints.Points.SCG.TransformBy(Matrix3d.Displacement(VectorLI))),
            SearchedAltitude);
    }
}
