namespace AutoBIMFusion.QuickPdf;

/// <summary>
///     Ожидаемая ошибка печати PDF (нет плоттера, рамка не помещается и т.п.).
/// </summary>
public sealed class QuickPdfException : InvalidOperationException
{
    public QuickPdfException(string message) : base(message)
    {
    }

    public QuickPdfException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
