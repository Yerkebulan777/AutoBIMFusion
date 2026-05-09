namespace AutoBIMFusion.Application.AcadSupport;

/// <summary>
/// RAII-скоуп для обхода бага AutoCAD при <c>WblockCloneObjects</c> и <c>Database.Insert</c>.
/// Когда у source и target разные <c>Insunits</c>/<c>Measurement</c> (метрическая ↔ имперская),
/// ядро AutoCAD скрыто применяет масштаб к клонируемой геометрии: появляются DSTYLE-переопределения
/// у размеров, плывут <c>ScaleFactor</c> блоков, штриховки и типы линий.
///
/// На время клонирования скоуп временно приравнивает единицы source к target (а не наоборот):
/// source — это всегда фоновый временный DWG, поэтому безопаснее менять его, а не постоянный target.
/// Гарантирует восстановление исходных значений source в <see cref="Dispose"/> (даже при исключении).
///
/// Дополнительно: отключает <c>Dimalt</c> у всех записей DimStyleTable в source.
/// Если Dimalt=true, AutoCAD может применить 25.4×/304.8× масштаб к визуальным
/// свойствам размеров через механизм дополнительных единиц, что приводит к «улетанию» текста.
/// </summary>
internal sealed class DatabaseUnitSyncScope : IDisposable
{
    private readonly Database _sourceDb;
    private readonly UnitsValue _savedSourceUnits;
    private readonly MeasurementValue _savedSourceMeasurement;

    // ObjectId → исходное значение Dimalt (только те, у кого Dimalt=true был)
    private readonly Dictionary<ObjectId, bool> _savedDimAltById = [];

    public DatabaseUnitSyncScope(Database sourceDb, Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(sourceDb);
        ArgumentNullException.ThrowIfNull(targetDb);

        _sourceDb = sourceDb;
        _savedSourceUnits = sourceDb.Insunits;
        _savedSourceMeasurement = sourceDb.Measurement;

        // Приравниваем source к target, чтобы WblockCloneObjects не применял 304.8× конверсию.
        _sourceDb.Insunits = targetDb.Insunits;
        _sourceDb.Measurement = targetDb.Measurement;

        DisableDimAltInSource();
    }

    public void Dispose()
    {
        _sourceDb.Insunits = _savedSourceUnits;
        _sourceDb.Measurement = _savedSourceMeasurement;

        RestoreDimAlt();
    }

    /// <summary>
    /// Итерирует DimStyleTable source-базы и отключает Dimalt у всех стилей, где он включён.
    /// Сохраняет исходные значения для восстановления в <see cref="RestoreDimAlt"/>.
    /// </summary>
    private void DisableDimAltInSource()
    {
        try
        {
            using Transaction trx = _sourceDb.TransactionManager.StartTransaction();

            DimStyleTable dst = (DimStyleTable)trx.GetObject(_sourceDb.DimStyleTableId, OpenMode.ForRead);

            foreach (ObjectId id in dst)
            {
                if (id.IsNull || id.IsErased)
                {
                    continue;
                }

                DimStyleTableRecord dsr = (DimStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);

                if (dsr.IsErased || !dsr.Dimalt)
                {
                    continue;
                }

                // Сохраняем и отключаем: Dimalt=true провоцирует 304.8× масштаб при клонировании.
                _savedDimAltById[id] = true;
                dsr.UpgradeOpen();
                dsr.Dimalt = false;
            }

            trx.Commit();
        }
        catch
        {
            // Не прерываем основную операцию — DimensionStyleNormalizer устранит оставшиеся overrides.
        }
    }

    /// <summary>
    /// Восстанавливает Dimalt у записей DimStyleTable source-базы.
    /// </summary>
    private void RestoreDimAlt()
    {
        if (_savedDimAltById.Count == 0)
        {
            return;
        }

        try
        {
            using Transaction trx = _sourceDb.TransactionManager.StartTransaction();

            foreach ((ObjectId id, bool dimAlt) in _savedDimAltById)
            {
                if (id.IsNull || id.IsErased)
                {
                    continue;
                }

                DimStyleTableRecord dsr = (DimStyleTableRecord)trx.GetObject(id, OpenMode.ForWrite);
                dsr.Dimalt = dimAlt;
            }

            trx.Commit();
        }
        catch
        {
            // Source-DB будет закрыт после операции, потеря состояния не критична.
        }
    }
}
