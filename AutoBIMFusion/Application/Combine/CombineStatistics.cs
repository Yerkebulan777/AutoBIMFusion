namespace AutoBIMFusion.Application.Combine;

/// <summary>
/// Статистика операции слияния DWG-файлов.
/// </summary>
internal sealed class CombineStatistics
{
    public int TotalFiles { get; private set; }
    public int Successful { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }

    public void RecordTotal()
    {
        TotalFiles++;
    }

    public void RecordSuccess()
    {
        Successful++;
    }

    public void RecordFailed()
    {
        Failed++;
    }

    public void RecordSkipped()
    {
        Skipped++;
    }


    public override string ToString()
    {
        return $"Всего: {TotalFiles}, Успешно: {Successful}, Пропущено: {Skipped}, Ошибок: {Failed}";
    }
}
