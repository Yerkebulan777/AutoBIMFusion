using System.Collections;

namespace AutoBIMFusion.QuickPdf.Plotting;

internal static class PlotLists
{
    public static IEnumerable<string> Names(IEnumerable items)
    {
        foreach (object? item in items)
        {
            if (item is string { Length: > 0 } name)
            {
                yield return name;
            }
        }
    }
}
