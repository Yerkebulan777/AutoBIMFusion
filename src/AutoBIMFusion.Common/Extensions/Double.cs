namespace SioForgeCAD.Commun.Extensions;

public static class DoubleExtensions
{
    public static double Clamp(this double value, double MinValue, double MaxValue)
    {
        return Max(Min(value, MaxValue), MinValue);
    }
}
