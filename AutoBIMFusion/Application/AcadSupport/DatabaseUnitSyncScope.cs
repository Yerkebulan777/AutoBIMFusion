namespace AutoBIMFusion.Application.AcadSupport;

/// <summary>
/// RAII-скоуп для обхода бага AutoCAD при <c>WblockCloneObjects</c> и <c>Database.Insert</c>.
/// Когда у source и target разные <c>Insunits</c>/<c>Measurement</c> (метрическая ↔ имперская),
/// ядро AutoCAD скрыто применяет масштаб к клонируемой геометрии: появляются DSTYLE-переопределения
/// у размеров, плывут <c>ScaleFactor</c> блоков, штриховки и типы линий.
/// На время клонирования скоуп выравнивает единицы source по target и гарантированно
/// возвращает оригинальные значения в <see cref="Dispose"/> (даже при исключении).
/// </summary>
internal sealed class DatabaseUnitSyncScope : IDisposable
{
    private readonly Database _sourceDb;
    private readonly UnitsValue _originalUnits;
    private readonly MeasurementValue _originalMeasurement;
    private readonly bool _originalDimalt;

    public DatabaseUnitSyncScope(Database sourceDb, Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(sourceDb);
        ArgumentNullException.ThrowIfNull(targetDb);

        _sourceDb = sourceDb;
        _originalUnits = sourceDb.Insunits;
        _originalMeasurement = sourceDb.Measurement;
        _originalDimalt = sourceDb.Dimalt;

        // Source подгоняется под target, чтобы WblockCloneObjects не создавал масштабные overrides.
        _sourceDb.Insunits = targetDb.Insunits;
        _sourceDb.Measurement = targetDb.Measurement;
        _sourceDb.Dimalt = targetDb.Dimalt;
    }

    public void Dispose()
    {
        _sourceDb.Insunits = _originalUnits;
        _sourceDb.Measurement = _originalMeasurement;
        _sourceDb.Dimalt = _originalDimalt;
    }
}
