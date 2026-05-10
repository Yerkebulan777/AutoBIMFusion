namespace AutoBIMFusion.Merge;

/// <summary>
/// Extension-методы для работы с ObjectId.
/// </summary>
internal static class ObjectIdExtensions
{
    /// <summary>
    /// Проверяет, что ObjectId валиден для операций (не null и не удалён).
    /// </summary>
    internal static bool IsValidForOperation(this ObjectId id)
        => !id.IsNull && !id.IsErased;
}
