using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Вставляет содержимое временных DWG как определения блоков и раскладывает вхождения вдоль оси X.
/// </summary>
internal sealed class BlockInserter(double gapPercent, OperationLogger log)
{
    private readonly double _gapPercent = gapPercent;
    private readonly OperationLogger _log = log;
    private HashSet<string>? _usedNames;
    private double _rightMax;

    public string BuildUniqueName(Database db, string baseName)
    {
        EnsureNamesLoaded(db);

        string sanitizedBase = LayoutUtil.SanitizeSymbolName(baseName);
        string name = sanitizedBase;
        int idx = 1;

        while (!_usedNames!.Add(name))
        {
            name = $"{sanitizedBase}_{idx}";
            idx++;
        }

        return name;
    }

    private void EnsureNamesLoaded(Database db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (_usedNames is not null)
        {
            return;
        }

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        _usedNames = bt
            .Cast<ObjectId>()
            .Select(id => (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead))
            .Select(btr => btr.Name)
            .ToHashSet();

        tr.Commit();

        _log.Info($"Инициализация BlockInserter: {_usedNames.Count} существующих блоков");
    }

    /// <summary>
    /// Присоединяет файл по указанному пути как внешнюю ссылку (XREF), размещает её вхождение
    /// и немедленно внедряет (Bind) в целевой чертёж в соответствии с настройками.
    /// Возвращает мировые границы вхождения или null при ошибке вставки.
    /// </summary>
    public Extents3d? InsertAndBindXref(Database targetDb, string sourceFilePath, string blockName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);

        try
        {
            ObjectId xrefId;
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                xrefId = targetDb.AttachXref(sourceFilePath, blockName);

                ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);

                BlockReference bref = new(insertPt, xrefId);
                bref.SetDatabaseDefaults();
                _ = ms.AppendEntity(bref);
                tr.AddNewlyCreatedDBObject(bref, true);

                tr.Commit();
            }

            using ObjectIdCollection xrefsToBind = [];
            _ = xrefsToBind.Add(xrefId);
            targetDb.BindXrefs(xrefsToBind, true);
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, $"BlockInserter: {blockName}");
            return null;
        }

        Extents3d worldBounds = new(
            new Point3d(insertPt.X + sourceBounds.MinPoint.X, insertPt.Y + sourceBounds.MinPoint.Y, 0),
            new Point3d(insertPt.X + sourceBounds.MaxPoint.X, insertPt.Y + sourceBounds.MaxPoint.Y, 0)
        );
        _rightMax = worldBounds.MaxPoint.X;
        return worldBounds;
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = bounds.MaxPoint.X - bounds.MinPoint.X;
        double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
        double gap = Math.Round(Math.Max(width, height) * _gapPercent, 0);

        double insertX = _rightMax > 0 ? _rightMax + gap - bounds.MinPoint.X : -bounds.MinPoint.X;
        Point3d insertPt = new(insertX, -bounds.MinPoint.Y, 0);

        _log.Debug($"Позиция вставки: X={insertPt.X:F2}, Y={insertPt.Y:F2}, gap={gap:F0}");
        return insertPt;
    }

}
