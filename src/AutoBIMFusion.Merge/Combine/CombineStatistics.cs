namespace AutoBIMFusion.Merge.Combine;

/// <summary>
/// Статистика операции слияния DWG-файлов.
/// </summary>
public sealed class CombineStatistics
{
    public int TotalFiles { get; private set; }
    public int Successful { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }

    public void AddTotal()
    {
        TotalFiles++;
    }

    public void AddSuccess()
    {
        Successful++;
    }

    public void AddFailed()
    {
        Failed++;
    }

    public void AddSkipped()
    {
        Skipped++;
    }

    public override string ToString()
    {
        return $"Всего: {TotalFiles}, Успешно: {Successful}, Пропущено: {Skipped}, Ошибок: {Failed}";
    }
}
