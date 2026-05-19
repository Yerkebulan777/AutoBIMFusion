namespace AutoBIMFusion.Common.AcadSupport;

/// <summary>
///     RAII-скоуп для обхода бага AutoCAD при <c>WblockCloneObjects</c> и <c>Database.Insert</c>.
///     Когда у source и target разные <c>Insunits</c>/<c>Measurement</c> (метрическая ↔ имперская),
///     ядро AutoCAD скрыто применяет масштаб к клонируемой геометрии: появляются DSTYLE-переопределения
///     у размеров, плывут <c>ScaleFactor</c> блоков, штриховки и типы линий.
///     На время клонирования скоуп выравнивает единицы source по target и гарантированно
///     возвращает оригинальные значения в <see cref="Dispose" /> (даже при исключении).
/// </summary>
public sealed class DatabaseUnitSyncScope : IDisposable
{
    private readonly bool _originalDimalt;
    private readonly MeasurementValue _originalMeasurement;
    private readonly UnitsValue _originalUnits;
    private readonly Database _sourceDb;

    public DatabaseUnitSyncScope(Database sourceDb, Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(sourceDb);
        ArgumentNullException.ThrowIfNull(targetDb);

        _sourceDb = sourceDb;
        _originalDimalt = sourceDb.Dimalt; // Dimalt влияет на масштаб размеров при клонировании
        _originalUnits = sourceDb.Insunits; // Сохраняем оригинальные единицы измерения, чтобы восстановить их в Dispose
        _originalMeasurement = sourceDb.Measurement; // Сохраняем оригинальную систему измерения, чтобы восстановить её в Dispose

        if (_sourceDb.Insunits != targetDb.Insunits)
        {
            _sourceDb.Insunits = UnitsValue.Millimeters;
        }

        if (_sourceDb.Measurement != MeasurementValue.Metric)
        {
            _sourceDb.Measurement = MeasurementValue.Metric;
        }

        _sourceDb.Dimalt = targetDb.Dimalt;
    }

    public void Dispose()
    {
        _sourceDb.Insunits = _originalUnits;
        _sourceDb.Measurement = _originalMeasurement;
        _sourceDb.Dimalt = _originalDimalt;
    }
}
