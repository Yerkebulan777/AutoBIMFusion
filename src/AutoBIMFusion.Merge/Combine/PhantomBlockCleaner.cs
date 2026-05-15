using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Обнаруживает и удаляет "фантомные" блоки — определения с микроскопической геометрией,
///     базовая точка которых смещена на миллионы единиц от начала координат.
///     Такие блоки искажают <see cref="ExtentsUtils.GetDatabaseExtents"/> и ломают пайплайн слияния.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PhantomBlockCleaner
{
    private const int MaxEntities = 10;
    private const double MaxPolylineLength = 10.0;
    private const double DefaultThresholdBase = 1000.0;
    private const double ThresholdMultiplier = 3.0;

    /// <summary>
    ///     Сканирует базу данных, удаляет вхождения фантомных блоков из Model Space.
    ///     Определения блоков не удаляются здесь — они очистятся позже через <see cref="DrawingPurger"/>.
    /// </summary>
    /// <param name="db">База данных исходного DWG-файла.</param>
    /// <param name="log">Экземпляр логгера Serilog.</param>
    public static void Clean(Database db, Logger log)
    {
        double threshold = ComputeOffsetThreshold(db);
        HashSet<ObjectId> phantomBtrs = FindPhantomBlocks(db, threshold, log);

        if (phantomBtrs.Count == 0)
            return;

        int erasedCount = EraseModelSpaceReferences(db, phantomBtrs);

        log.Information(
            "PhantomBlockCleaner: удалено {ErasedCount} вхождений {DefinitionCount} фантомных блоков",
            erasedCount, phantomBtrs.Count);
    }

    /// <summary>
    ///     Вычисляет порог аномального смещения на основе максимальной диагонали Paper Space листов.
    ///     Fallback: <see cref="DefaultThresholdBase"/> единиц, если листы отсутствуют или имеют нулевой размер.
    /// </summary>
    private static double ComputeOffsetThreshold(Database db)
    {
        double maxDiagonal = 0.0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        foreach (DBDictionaryEntry entry in layoutDict)
        {
            Layout layout = (Layout)trx.GetObject(entry.Value, OpenMode.ForRead);

            if (layout.ModelType)
                continue;

            var paperSize = layout.PlotPaperSize;
            double diagonal = Sqrt(paperSize.X * paperSize.X + paperSize.Y * paperSize.Y);

            if (diagonal > maxDiagonal)
                maxDiagonal = diagonal;
        }

        trx.Commit();

        double thresholdBase = maxDiagonal > 1.0 ? maxDiagonal : DefaultThresholdBase;
        return thresholdBase * ThresholdMultiplier;
    }

    /// <summary>
    ///     Проходит по всем пользовательским определениям блоков и возвращает идентификаторы фантомных.
    ///     Использует двухпроходной алгоритм: сначала дешёвая проверка типов через DxfName (без открытия
    ///     объектов), затем более дорогая проверка длины полилиний и геометрического смещения.
    /// </summary>
    private static HashSet<ObjectId> FindPhantomBlocks(Database db, double threshold, Logger log)
    {
        HashSet<ObjectId> result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();

        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in bt)
        {
            if (!btrId.IsValid || btrId.IsErased)
                continue;

            BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

            if (btr.IsLayout || btr.IsFromExternalReference || btr.Name.StartsWith("*"))
                continue;

            // Pass 1: быстрый счёт и проверка типов через DxfName — объекты не открываем
            int count = 0;
            bool allValidTypes = true;

            foreach (ObjectId entId in btr)
            {
                if (entId.IsErased)
                    continue;

                count++;

                if (count > MaxEntities)
                {
                    allValidTypes = false;
                    break;
                }

                string dxfName = entId.ObjectClass.DxfName;
                if (dxfName != "LWPOLYLINE" && dxfName != "LINE" && dxfName != "ARC")
                {
                    allValidTypes = false;
                    break;
                }
            }

            if (count < 1 || !allValidTypes)
                continue;

            // Pass 2: открываем объекты — проверка длины полилиний и накопление габаритов
            Extents3d? combined = null;
            bool lengthOk = true;

            foreach (ObjectId entId in btr)
            {
                if (entId.IsErased)
                    continue;

                Entity ent = (Entity)trx.GetObject(entId, OpenMode.ForRead);

                if (ent is Polyline pl && pl.Length > MaxPolylineLength)
                {
                    lengthOk = false;
                    break;
                }

                Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
                if (ext is null)
                    continue;

                combined = combined is null ? ext.Value : ExtentsUtils.Union(combined.Value, ext.Value);
            }

            if (!lengthOk || combined is null)
                continue;

            // Pass 3: проверка расстояния от центра геометрии до начала координат
            Point3d center = new(
                (combined.Value.MinPoint.X + combined.Value.MaxPoint.X) / 2.0,
                (combined.Value.MinPoint.Y + combined.Value.MaxPoint.Y) / 2.0,
                (combined.Value.MinPoint.Z + combined.Value.MaxPoint.Z) / 2.0);

            double displacement = center.DistanceTo(Point3d.Origin);

            if (displacement > threshold)
            {
                result.Add(btrId);
                log.Debug(
                    "PhantomBlockCleaner: фантомный блок «{Name}», смещение {Displacement:F0} ед.",
                    btr.Name, displacement);
            }
        }

        trx.Commit();
        return result;
    }

    /// <summary>
    ///     Стирает все вхождения (<see cref="BlockReference"/>) фантомных блоков из Model Space.
    /// </summary>
    /// <returns>Количество удалённых вхождений.</returns>
    private static int EraseModelSpaceReferences(Database db, HashSet<ObjectId> phantomBtrs)
    {
        int count = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId entId in ms)
        {
            if (entId.IsErased)
                continue;

            if (entId.ObjectClass.DxfName != "INSERT")
                continue;

            BlockReference br = (BlockReference)trx.GetObject(entId, OpenMode.ForRead);

            if (!phantomBtrs.Contains(br.BlockTableRecord))
                continue;

            br.UpgradeOpen();
            br.Erase();
            count++;
        }

        trx.Commit();
        return count;
    }
}
