namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Общие алгоритмические утилиты: бинарный поиск и т.п.
/// </summary>
public static class AlgorithmUtils
{
    /// <summary>
    ///     Находит первый индекс в отсортированном списке, где key(selector(list[i])) >= value.
    ///     Обобщённый lower-bound бинарный поиск.
    /// </summary>
    /// <typeparam name="T">Тип элемента списка.</typeparam>
    /// <param name="list">Отсортированный по ключу список.</param>
    /// <param name="value">Искомое значение ключа.</param>
    /// <param name="selector">Функция извлечения ключа из элемента.</param>
    /// <returns>Индекс первого элемента с ключом >= value, или list.Count если такого нет.</returns>
    public static int LowerBound<T>(IReadOnlyList<T> list, double value, Func<T, double> selector)
    {
        var left = 0;
        var right = list.Count;

        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (selector(list[middle]) < value)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }
}
