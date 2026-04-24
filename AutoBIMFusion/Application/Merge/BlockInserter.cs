using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Вставляет содержимое временных DWG в целевой чертёж в виде блоков (BlockReference),
/// располагая их вдоль оси X с заданным зазором. Использует DuplicateRecordCloning.Ignore
/// для предотвращения перезаписи стилей и слоёв.
/// </summary>
internal sealed class BlockInserter(double gapPercent, OperationLogger log)
{
    private readonly double _gapPercent = gapPercent;
    private readonly OperationLogger _log = log;
    private double _rightMax;
    private bool _hasPlacedObjects;

    /// <summary>
    /// Открывает временный DWG, клонирует все объекты из его Model Space
    /// в новый BlockTableRecord целевого чертежа и вставляет единственный
    /// BlockReference в Model Space с учётом смещения.
    /// Возвращает мировые границы вставленного блока или null при ошибке.
    /// </summary>
    public Extents3d? InsertNativeObjects(Database targetDb, string sourceFilePath, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            using Database sourceDb = new(false, true);
            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            ObjectIdCollection sourceIds = [];
            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);

            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIds.Add(id);
                    }
                }

                tr.Commit();
            }

            if (sourceIds.Count == 0)
            {
                _log.Warn($"BlockInserter: {sourceName} — Model Space пуст");
                return null;
            }

            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            string blockName;
            Extents3d? worldBounds = null;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForWrite);
                blockName = GetUniqueBlockName(bt, sourceName);

                BlockTableRecord btr = new BlockTableRecord
                {
                    Name = blockName
                };
                bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                IdMapping map = [];
                sourceDb.WblockCloneObjects(sourceIds, btr.ObjectId, map, DuplicateRecordCloning.Ignore, false);

                int clonedCount = 0;
                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary)
                    {
                        continue;
                    }

                    if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity)
                    {
                        clonedCount++;
                    }
                }

                if (clonedCount == 0)
                {
                    _log.Warn($"BlockInserter: {sourceName} — не удалось клонировать объекты");
                    tr.Commit();
                    return null;
                }

                BlockReference br = new BlockReference(Point3d.Origin, btr.ObjectId);
                br.TransformBy(displacement);

                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForWrite);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                worldBounds = GeometryUtils.TryGetExtents(br);
                if (!worldBounds.HasValue)
                {
                    Point3d min = new(insertPt.X + sourceBounds.MinPoint.X, insertPt.Y + sourceBounds.MinPoint.Y, 0);
                    Point3d max = new(insertPt.X + sourceBounds.MaxPoint.X, insertPt.Y + sourceBounds.MaxPoint.Y, 0);
                    worldBounds = new Extents3d(min, max);
                }

                tr.Commit();
            }

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;
            _log.Info($"BlockInserter: {sourceName} — вставлено блоком '{blockName}' ({sourceIds.Count} объектов)");
            return worldBounds;
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, $"BlockInserter: {sourceName}");
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = Math.Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        double height = Math.Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        double maxDimension = Math.Max(width, height);
        double gap = Math.Max(1.0, Math.Round(maxDimension * _gapPercent, 0));

        double insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;
        Point3d insertPt = new(insertX, -bounds.MinPoint.Y, 0);

        _log.Debug($"Позиция вставки: X={insertPt.X:F2}, Y={insertPt.Y:F2}, gap={gap:F0}");
        return insertPt;
    }

    private static string GetUniqueBlockName(BlockTable bt, string sourceName)
    {
        string safeName = SanitizeBlockName(sourceName);
        string baseName = $"Merge_{safeName}";
        string name = baseName;
        int counter = 1;

        while (bt.Has(name))
        {
            name = $"{baseName}_{counter}";
            counter++;
        }

        return name;
    }

    private static string SanitizeBlockName(string name)
    {
        char[] invalid = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', ';', '`'];
        string result = name.Trim();

        foreach (char c in invalid)
        {
            result = result.Replace(c, '_');
        }

        const int maxLength = 250;
        if (result.Length > maxLength)
        {
            result = result[..maxLength];
        }

        return result;
    }
}
