using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;

public class PolyHole : IDisposable
{
    public Polyline Boundary;
    public List<Polyline> Holes;

    public PolyHole(Polyline boundary, IEnumerable<Polyline> holes)
    {
        Boundary = boundary;
        Holes = holes != null ? holes.ToList() : [];
    }

    public void Dispose()
    {
        Boundary.Dispose();
        Holes.DeepDispose();
        GC.SuppressFinalize(this);
    }

    public static List<PolyHole> CreateFromList(IEnumerable<Polyline> polylines,
        IEnumerable<Polyline> PossibleHole = null)
    {
        List<PolyHole> polyholes = [];
        foreach (Polyline poly in polylines)
        {
            List<Polyline> holes = [];
            if (PossibleHole != null)
            {
                foreach (Polyline? Hole in PossibleHole)
                {
                    if (Hole?.IsDisposed != true && Hole.IsInside(poly, false))
                    {
                        holes.Add(Hole);
                    }
                }
            }

            polyholes.Add(new PolyHole(poly, holes));
        }

        return polyholes;
    }
}

public static class PolyHoleExtensions
{
    public static List<Polyline> GetBoundaries(this IEnumerable<PolyHole> polyHolesList)
    {
        List<Polyline> holes = [];
        foreach (PolyHole item in polyHolesList)
        {
            holes.Add(item.Boundary);
        }

        return holes;
    }

    public static List<Polyline> GetAllHoles(this IEnumerable<PolyHole> polyHolesList)
    {
        List<Polyline> holes = [];
        foreach (PolyHole item in polyHolesList)
        {
            holes.AddRange(item.Holes);
        }

        return holes;
    }
}
