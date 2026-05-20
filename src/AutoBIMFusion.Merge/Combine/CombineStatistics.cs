namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Статистика операции слияния DWG-файлов.
/// </summary>
public sealed class CombineStatistics
{
    public int TotalFiles { get; private set; }
    public int Successful { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }

    public void Update(CombineResult result)
    {
        TotalFiles++;
        if (result.Success) Successful++;
        else if (result.IsSkipped) Skipped++;
        else Failed++;
    }

    public override string ToString() => $"Всего: {TotalFiles}, Успешно: {Successful}, Пропущено: {Skipped}, Ошибок: {Failed}";
}
