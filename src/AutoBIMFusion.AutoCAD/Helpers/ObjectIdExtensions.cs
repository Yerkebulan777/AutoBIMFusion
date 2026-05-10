namespace AutoBIMFusion.AutoCAD.Helpers;

/// <summary>
/// Extension-методы для работы с ObjectId.
/// </summary>
public static class ObjectIdExtensions
{
    /// <summary>
    /// Проверяет, что ObjectId валиден для операций (не null и не удалён).
    /// </summary>
    public static bool IsValidForOperation(this ObjectId id)
        => !id.IsNull && !id.IsErased;
}
