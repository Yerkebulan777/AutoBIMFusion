using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Commun;

public static class Arythmetique
{
    public static (double Altitude, double Slope) ComputeSlopeAndIntermediate(CotePoints First, CotePoints Second,
        Points Intermediaire)
    {
        if (First is null || Second is null) return (0, 0);
        var FirstPointSCG = First.Points.SCG;
        var SecondPointSCG = Second.Points.SCG;
        var IntermediairePointInSCG = Intermediaire.SCG;

        //calcul distance :
        var AI_dist_horizontal = Pow(IntermediairePointInSCG.X - FirstPointSCG.X, 2);
        var AI_dist_vertical = Pow(IntermediairePointInSCG.Y - FirstPointSCG.Y, 2);
        var AI_dist_total = Sqrt(AI_dist_horizontal + AI_dist_vertical);

        var IB_dist_horizontal = Pow(SecondPointSCG.X - IntermediairePointInSCG.X, 2);
        var IB_dist_vertical = Pow(SecondPointSCG.Y - IntermediairePointInSCG.Y, 2);
        var IB_dist_total = Sqrt(IB_dist_horizontal + IB_dist_vertical);

        var AB_dist_horizontal = Pow(SecondPointSCG.X - FirstPointSCG.X, 2);
        var AB_dist_vertical = Pow(SecondPointSCG.Y - FirstPointSCG.Y, 2);
        var AB_dist_total = Sqrt(AB_dist_horizontal + AB_dist_vertical);

        var AIB_dist_total = AI_dist_total + IB_dist_total;

        var AI_pourcent = AI_dist_total / AIB_dist_total;
        var AB_cote_dif = Abs(First.Altitude - Second.Altitude);
        var I_dif_to_add_sus = AB_cote_dif * AI_pourcent;
        var I_cote = First.Altitude;

        var pente = Round(AB_cote_dif / AB_dist_total * 100.00, 2);

        if (First.Altitude > Second.Altitude)
            I_cote -= I_dif_to_add_sus;
        else
            I_cote += I_dif_to_add_sus;

        if (double.IsNaN(I_cote)) I_cote = First.Altitude;
        if (double.IsNaN(pente)) pente = 0;

        return (I_cote, pente);
    }

    public static double ComputePointFromSlopePourcentage(double OriginAltitude, double DistanceFromOrigin,
        double Slope)
    {
        const double PourcentageToDecimalRatio = 0.01;
        var Altimetrie = Abs(OriginAltitude) + Slope * PourcentageToDecimalRatio * Abs(DistanceFromOrigin);
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

        var LUDistance = LowestCotePoints.Points.SCG.DistanceTo(UpperCotePoints.Points.SCG);

        var LUDiffAltitude = UpperCotePoints.Altitude - LowestCotePoints.Altitude;
        var LIDiffAltitude = SearchedAltitude - LowestCotePoints.Altitude;
        var DistRatio = LIDiffAltitude / LUDiffAltitude;
        var DistFromLowest = DistRatio * LUDistance;


        //var DistanceLI = LowestCotePoints.Altitude - SearchedAltitude;
        //if (DistanceLI == 0 || DistanceFS == 0) { return CotePoints.Null; }
        //var DistRatio = DistanceLI / DistanceFS;
        //var DistFromLowest = DistanceFS * DistRatio;
        var VectorLI = LowestCotePoints.Points.SCG.GetVectorTo(UpperCotePoints.Points.SCG).SetLength(DistFromLowest);
        return new CotePoints(
            Points.From3DPoint(LowestCotePoints.Points.SCG.TransformBy(Matrix3d.Displacement(VectorLI))),
            SearchedAltitude);
    }
}
