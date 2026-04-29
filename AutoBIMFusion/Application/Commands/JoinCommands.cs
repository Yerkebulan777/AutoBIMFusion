using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class JoinCommands
{
    private const double MaxLineLength = 10000.0;
    private const double Tol = 1e-6;
    private const int DirDecimals = 6;
    private const int OffDecimals = 4;

    [CommandMethod("JOIN_LINES")]
    public static void JoinLinesCommand()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        AILog log = new(doc.Editor);
        log.Info("Запуск команды JOIN_LINES...");

        try
        {
            using (doc.LockDocument())
            {
                Database db = doc.Database;

                using Transaction tr = db.TransactionManager.StartTransaction();

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                List<LineInfo> candidates = [];

                foreach (ObjectId id in modelSpace)
                {
                    if (id.ObjectClass.DxfName != "LINE")
                    {
                        continue;
                    }

                    if (tr.GetObject(id, OpenMode.ForRead) is not Line line)
                    {
                        continue;
                    }

                    double len = line.Length;
                    if (len is > MaxLineLength or < Tol)
                    {
                        continue;
                    }

                    candidates.Add(new LineInfo(line));
                }

                if (candidates.Count < 2)
                {
                    log.Info("Недостаточно линий для объединения.");
                    tr.Commit();
                    return;
                }

                List<IGrouping<GroupKey, LineInfo>> groups = candidates
                    .GroupBy(c => c.Key)
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (groups.Count == 0)
                {
                    log.Info("Группы для объединения не найдены.");
                    tr.Commit();
                    return;
                }

                BlockTableRecord modelSpaceWrite = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int joinedGroups = 0;
                int joinedLines = 0;

                foreach (IGrouping<GroupKey, LineInfo> group in groups)
                {
                    List<Segment> merged = MergeGroup(group.ToList());

                    if (merged.Count >= group.Count())
                    {
                        continue;
                    }

                    LineInfo proto = group.First();

                    foreach (Segment seg in merged)
                    {
                        Line newLine = new(seg.Start, seg.End)
                        {
                            Layer = proto.Entity.Layer,
                            ColorIndex = proto.Entity.ColorIndex,
                            LinetypeId = proto.Entity.LinetypeId,
                            LinetypeScale = proto.Entity.LinetypeScale,
                            LineWeight = proto.Entity.LineWeight,
                            Transparency = proto.Entity.Transparency
                        };

                        _ = modelSpaceWrite.AppendEntity(newLine);
                        tr.AddNewlyCreatedDBObject(newLine, true);
                    }

                    foreach (LineInfo info in group)
                    {
                        info.Entity.UpgradeOpen();
                        info.Entity.Erase();
                    }

                    joinedGroups++;
                    joinedLines += group.Count();
                }

                tr.Commit();

                if (joinedGroups > 0)
                {
                    log.Info($"Объединено групп: {joinedGroups}, исходных линий: {joinedLines}");
                }
                else
                {
                    log.Info("Ни одна группа не была объединена (возможно, линии уже разделены оптимально).");
                }
            }

            log.Info("Завершение команды JOIN_LINES.");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения JOIN_LINES.");
        }
    }

    private static List<Segment> MergeGroup(List<LineInfo> lines)
    {
        Vector3d dir = lines[0].Direction;

        var projected = lines
            .Select(l =>
            {
                Vector3d sv = new(l.Start.X, l.Start.Y, l.Start.Z);
                Vector3d ev = new(l.End.X, l.End.Y, l.End.Z);
                double t1 = sv.DotProduct(dir);
                double t2 = ev.DotProduct(dir);
                return new { Min = Min(t1, t2), Max = Max(t1, t2) };
            })
            .OrderBy(p => p.Min)
            .ToList();

        List<Segment> result = [];
        double curMin = projected[0].Min;
        double curMax = projected[0].Max;

        for (int i = 1; i < projected.Count; i++)
        {
            var p = projected[i];

            if (p.Min <= curMax + Tol)
            {
                if (p.Max > curMax)
                {
                    curMax = p.Max;
                }
            }
            else
            {
                result.Add(new Segment(lines[0].Offset + (dir * curMin), lines[0].Offset + (dir * curMax)));
                curMin = p.Min;
                curMax = p.Max;
            }
        }

        result.Add(new Segment(lines[0].Offset + (dir * curMin), lines[0].Offset + (dir * curMax)));
        return result;
    }

    private readonly record struct GroupKey(string Layer, int ColorIndex, string DirKey, string OffKey);

    private readonly record struct Segment(Point3d Start, Point3d End);

    private sealed class LineInfo
    {
        public Line Entity { get; }
        public Point3d Start { get; }
        public Point3d End { get; }
        public Vector3d Direction { get; }
        public Point3d Offset { get; }
        public GroupKey Key { get; }

        public LineInfo(Line line)
        {
            Entity = line;
            Start = line.StartPoint;
            End = line.EndPoint;

            Vector3d raw = End - Start;
            Direction = raw.GetNormal();

            // Канонизация направления: переворачиваем вектор в одну сторону,
            // чтобы (1,0,0) и (-1,0,0) считались одним ключом
            if (Direction.X < -Tol
                || (Abs(Direction.X) <= Tol && Direction.Y < -Tol)
                || (Abs(Direction.X) <= Tol && Abs(Direction.Y) <= Tol && Direction.Z < -Tol))
            {
                Direction = -Direction;
            }

            Vector3d sv = new(Start.X, Start.Y, Start.Z);
            double proj = sv.DotProduct(Direction);
            Offset = Start - (Direction * proj);

            string dirKey = $"{Round(Direction.X, DirDecimals):F6};{Round(Direction.Y, DirDecimals):F6};{Round(Direction.Z, DirDecimals):F6}";
            string offKey = $"{Round(Offset.X, OffDecimals):F4};{Round(Offset.Y, OffDecimals):F4};{Round(Offset.Z, OffDecimals):F4}";

            Key = new GroupKey(
                line.Layer,
                line.ColorIndex,
                dirKey,
                offKey);
        }
    }
}
