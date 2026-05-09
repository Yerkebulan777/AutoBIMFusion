namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Устраняет DSTYLE-переопределения на клонированных размерах и приводит их к стандарту ГОСТ.
///
/// Проблема: WblockCloneObjects может добавить инстанс-уровневые DSTYLE-overrides с коэффициентом
/// 304.8 (мм ↔ футы), в том числе Dimgap × 304.8, Dimtxt × 304.8, Dimscale × 304.8.
/// DatabaseUnitSyncScope предотвращает большинство таких случаев, но не все (например, Dimalt-пути).
///
/// Данный класс — второй рубеж защиты: явно выставляет ВСЕ визуальные dim-переменные
/// в корректные ГОСТ-значения ПОСЛЕ клонирования, полностью перекрывая любые 304.8×-overrides.
/// </summary>
internal static class DimensionStyleNormalizer
{
    // ГОСТ-параметры: совпадают с StyleUnificationService.ApplyStandardDimensionStyleSettings
    private const double GostDimgap  = 1.0;    // отступ текста от размерной линии — ключевой параметр
    private const double GostDimtxt  = 2.5;    // высота текста
    private const double GostDimasz  = 2.5;    // размер стрелки/засечки
    private const double GostDimtsz  = 2.5;    // размер засечки (ГОСТ-архитектурный тип)
    private const double GostDimexe  = 1.25;   // вынос выносной линии
    private const double GostDimexo  = 0.625;  // отступ выносной линии от объекта

    /// <summary>
    /// Итерирует клонированные объекты из <paramref name="idMap"/>, находит все Dimension,
    /// назначает ГОСТ-стиль и явно сбрасывает все визуальные dim-переменные.
    /// Вызывать ПОСЛЕ TransformBy, чтобы RecomputeDimensionBlock работал с финальными координатами.
    /// </summary>
    /// <param name="idMap">Маппинг объектов после WblockCloneObjects.</param>
    /// <param name="trx">Открытая транзакция target-базы.</param>
    /// <param name="gostDimStyleId">ObjectId ГОСТ-стиля (AutoBIM-ISOCPEUR или аналог).</param>
    /// <param name="linearScaleMultiplier">Коэффициент Dimlfac для отображения измерения (обычно 1.0).</param>
    internal static void NormalizeClonedDimensions(
        IdMapping idMap,
        Transaction trx,
        ObjectId gostDimStyleId,
        double linearScaleMultiplier)
    {
        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || !pair.IsPrimary)
            {
                continue;
            }

            if (pair.Value.IsNull || pair.Value.IsErased)
            {
                continue;
            }

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim)
            {
                continue;
            }

            // Назначаем ГОСТ-стиль как базовый (стилевые значения — fallback для несброшенных свойств).
            dim.DimensionStyle = gostDimStyleId;

            // Очищаем XData — может содержать AEC/MEP-специфичные overrides.
            dim.XData = null;

            // Сбрасываем ВСЕ визуальные dim-переменные, которые WblockCloneObjects мог умножить на 304.8.
            // Инстанс-уровневые overrides всегда выигрывают у стилевых значений,
            // поэтому явное присваивание обязательно — dim.DimensionStyle = id сам по себе не помогает.
            dim.Dimgap   = GostDimgap;   // ← главный fix «улетающего текста»: 304.8 → 1.0
            dim.Dimtxt   = GostDimtxt;
            dim.Dimasz   = GostDimasz;
            dim.Dimtsz   = GostDimtsz;
            dim.Dimscale = 1.0;          // визуальный масштаб уже запечён в геометрию при трансформации
            dim.Dimlfac  = linearScaleMultiplier;
            dim.Dimexe   = GostDimexe;
            dim.Dimexo   = GostDimexo;
            dim.Dimtad   = 1;            // текст над размерной линией (ГОСТ)
            dim.Dimtih   = false;        // текст параллелен размерной линии (ГОСТ)
            dim.Dimtoh   = false;        // текст параллелен размерной линии вне размерных линий (ГОСТ)

            // Перестраиваем блок размера с применением всех обновлённых значений.
            dim.RecomputeDimensionBlock(true);
        }
    }
}
