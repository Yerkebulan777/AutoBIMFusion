using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class SmartTextCommands
{
    private const double WordWidthFactor = 1.5; // Коэффициент допуска по ширине текста
    private const double LineHeightFactor = 2.0; // Увеличено для объединения многострочных текстов

    [CommandMethod("SMART_MERGE_TEXT")]
    public void SmartMergeModelText()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        OperationLogger log = new(doc.Editor);
        log.Info("Запуск команды SMART_MERGE_TEXT...");
        Database db = doc.Database;
        int mergedGroupsCount = 0;

        try
        {
            using (doc.LockDocument())
            {
                using Transaction tr = db.TransactionManager.StartTransaction();

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                List<TextElement> textsInModel = CollectTextElements(modelSpace, tr, log);

                if (textsInModel.Count == 0)
                {
                    log.Info("Текст (TEXT или MTEXT) в Model Space не найден.");
                    tr.Commit();
                    return;
                }

                List<List<TextElement>> groups = SmartGroupText(textsInModel);

                BlockTableRecord modelSpaceWrite = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (List<TextElement> group in groups.Where(g => g.Count > 1))
                {
                    (string? combinedString, TextElement? topLeftElement) = CombineGroupText(group);

                    MText mergedText = new()
                    {
                        Contents = combinedString,
                        Location = topLeftElement.Position,
                        Layer = topLeftElement.Layer,
                        TextStyleId = topLeftElement.TextStyleId,
                        TextHeight = topLeftElement.Height,
                        Rotation = topLeftElement.Rotation,
                        Normal = topLeftElement.Normal,
                        Color = topLeftElement.Color,
                        Width = 0
                    };

                    _ = modelSpaceWrite.AppendEntity(mergedText);
                    tr.AddNewlyCreatedDBObject(mergedText, true);

                    foreach (TextElement text in group)
                    {
                        text.OriginalEntity.UpgradeOpen();
                        text.OriginalEntity.Erase();
                    }

                    mergedGroupsCount++;
                }

                tr.Commit();
                log.Info($"SMART_MERGE_TEXT: собрано групп текста: {mergedGroupsCount}");
            }

            log.Info("Завершение команды SMART_MERGE_TEXT.");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения SMART_MERGE_TEXT.");
        }
    }

    private static List<TextElement> CollectTextElements(BlockTableRecord modelSpace, Transaction tr, OperationLogger log)
    {
        List<TextElement> result = [];
        int textCount = 0;
        int mtextCount = 0;
        int otherCount = 0;

        foreach (ObjectId id in modelSpace)
        {
            string dxfName = id.ObjectClass.DxfName;

            if (dxfName is not "TEXT" and not "MTEXT")
            {
                otherCount++;
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (ent is MText mText)
            {
                mtextCount++;
                if (string.IsNullOrWhiteSpace(mText.Text))
                {
                    mText.UpgradeOpen();
                    mText.Erase();
                    continue;
                }

                result.Add(new TextElement(
                    mText.ObjectId,
                    mText.Text,
                    mText.Location,
                    mText.TextStyleId,
                    mText.TextHeight,
                    mText.Rotation,
                    mText.Normal,
                    mText.Layer,
                    mText.Color,
                    mText
                ));
            }
            else if (ent is DBText dbText)
            {
                textCount++;
                if (string.IsNullOrWhiteSpace(dbText.TextString))
                {
                    dbText.UpgradeOpen();
                    dbText.Erase();
                    continue;
                }

                result.Add(new TextElement(
                    dbText.ObjectId,
                    dbText.TextString,
                    dbText.Position,
                    dbText.TextStyleId,
                    dbText.Height,
                    dbText.Rotation,
                    dbText.Normal,
                    dbText.Layer,
                    dbText.Color,
                    dbText
                ));
            }
        }

        log.Info($"Диагностика Model Space: найдено однострочных TEXT: {textCount}, многострочных MTEXT: {mtextCount}, других объектов: {otherCount}");

        return result;
    }

    /// <summary>
    /// Алгоритм группировки текстов в логические абзацы (предложения).
    /// 1. Первичный фильтр: объединяет тексты с одинаковым стилем и углом поворота.
    /// 2. Геометрический поиск (O(N log N) благодаря сортировке):
    ///    Тексты сортируются по перпендикуляру (условно координата Y относительно угла поворота).
    ///    Для каждого текста ищутся соседи, которые удовлетворяют двум условиям (AreTextsClose):
    ///    - Находятся по вертикали на одной строке или на соседних строках (допуск LineHeightFactor).
    ///    - Находятся по горизонтали достаточно близко (допуск зависит от ширины текста, WordWidthFactor).
    ///    - При этом высота текстов не должна различаться более чем на 15%.
    /// </summary>
    private static List<List<TextElement>> SmartGroupText(List<TextElement> texts)
    {
        List<List<TextElement>> groups = [];
        HashSet<ObjectId> visited = [];

        // Группируем тексты по стилю и углу поворота (первичные фильтры)
        // Высоту больше не группируем жестко, а проверяем как относительный допуск при поиске соседей,
        // чтобы тексты высотой 10 и 500 имели пропорциональный допуск на погрешность высоты.
        var preFilteredGroups = texts
            .GroupBy(t => new { t.TextStyleId, Rotation = Math.Round(t.Rotation, 3) })
            .ToList();

        foreach (var preGroup in preFilteredGroups)
        {
            List<TextElement> candidates = preGroup.ToList();
            double rotation = preGroup.Key.Rotation;
            double cosA = Math.Cos(rotation);
            double sinA = Math.Sin(rotation);

            // Сортируем кандидатов по координате перпендикулярной строке (условно "Y" строки)
            // Это позволит нам сравнивать текст только с соседями по вертикали.
            var sortedCandidates = candidates
                .Select(t => new { Text = t, Perp = (-t.Position.X * sinA) + (t.Position.Y * cosA) })
                .OrderBy(item => item.Perp)
                .ToList();

            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                var startItem = sortedCandidates[i];
                if (!visited.Add(startItem.Text.ObjectId))
                {
                    continue;
                }

                List<TextElement> currentGroup = [];
                Queue<TextElement> queue = new();
                queue.Enqueue(startItem.Text);

                while (queue.Count > 0)
                {
                    TextElement current = queue.Dequeue();
                    currentGroup.Add(current);

                    double curPerp = (-current.Position.X * sinA) + (current.Position.Y * cosA);
                    double verticalThreshold = current.Height * LineHeightFactor;

                    // Ищем соседей только в ограниченном диапазоне по Perp
                    // Так как sortedCandidates отсортирован, мы можем искать в обе стороны от i
                    // Но проще и эффективнее в рамках этой очереди проверить ближайших соседей в списке.

                    // Для простоты реализации мы проходим по соседям в sortedCandidates.
                    for (int j = 0; j < sortedCandidates.Count; j++)
                    {
                        var otherItem = sortedCandidates[j];
                        if (visited.Contains(otherItem.Text.ObjectId))
                        {
                            continue;
                        }

                        // Тексты должны быть близкого размера (допускаем 10% разницы, как запрошено)
                        double minH = Math.Min(current.Height, otherItem.Text.Height);
                        double maxH = Math.Max(current.Height, otherItem.Text.Height);
                        if (minH / maxH < 0.90)
                        {
                            continue;
                        }

                        // Если мы вышли за вертикальный порог...
                        if (Math.Abs(curPerp - otherItem.Perp) > verticalThreshold)
                        {
                            continue;
                        }

                        if (!AreTextsClose(current, otherItem.Text))
                        {
                            continue;
                        }

                        if (visited.Add(otherItem.Text.ObjectId))
                        {
                            queue.Enqueue(otherItem.Text);
                        }
                    }
                }

                groups.Add(currentGroup);
            }
        }

        return groups;
    }

    /// <summary>
    /// Группирует тексты по строкам и сортирует каждую строку слева направо.
    /// Возвращает объединенный текст с разделителями строк \P и левый верхний элемент для позиционирования.
    /// </summary>
    private static (string CombinedString, TextElement TopLeftElement) CombineGroupText(List<TextElement> group)
    {
        double rotation = group[0].Rotation;
        double cosA = Math.Cos(rotation);
        double sinA = Math.Sin(rotation);

        // Сортировка сверху вниз (Perp по убыванию)
        List<TextElement> perpSorted = group
            .OrderByDescending(t => (-t.Position.X * sinA) + (t.Position.Y * cosA))
            .ToList();

        List<List<TextElement>> lines = new();
        List<TextElement> currentLine = new();

        double currentLinePerp = (-perpSorted[0].Position.X * sinA) + (perpSorted[0].Position.Y * cosA);
        double lineThreshold = perpSorted[0].Height * 0.5; // Порог для объединения текстов в одну строку

        foreach (TextElement? t in perpSorted)
        {
            double perp = (-t.Position.X * sinA) + (t.Position.Y * cosA);
            if (Math.Abs(perp - currentLinePerp) <= lineThreshold)
            {
                currentLine.Add(t);
            }
            else
            {
                lines.Add(currentLine);
                currentLine = [t];
                currentLinePerp = perp;
            }
        }
        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        List<string> combinedLines = new();
        TextElement? topLeftElement = null;

        foreach (List<TextElement> line in lines)
        {
            // Сортировка слева направо (Parallel по возрастанию)
            List<TextElement> sortedLine = line.OrderBy(t => (t.Position.X * cosA) + (t.Position.Y * sinA)).ToList();

            if (topLeftElement == null)
            {
                topLeftElement = sortedLine[0];
            }

            combinedLines.Add(string.Join(" ", sortedLine.Select(t => EscapeMTextContent(t.TextString))));
        }

        return (string.Join("\\P", combinedLines), topLeftElement!);
    }

    /// <summary>
    /// Проверяет, можно ли объединить два текстовых элемента в один абзац (группу).
    /// 
    /// Логика проверок:
    /// 1. Вертикальный зазор (dy): проекция на перпендикулярную ось (относительно угла текста)
    ///    не должна превышать (максимальная высота * LineHeightFactor). Это позволяет объединять
    ///    соседние строки многострочного текста.
    /// 
    /// 2. Горизонтальный зазор (gap): вычисляются чистые границы (Left, Right) обоих текстов
    ///    на их параллельной оси (ось направления текста). Если тексты пересекаются, gap = 0.
    ///    Если между ними есть просвет, gap будет равен физическому расстоянию между краями.
    ///    Зазор не должен превышать (максимальная ширина текста * WordWidthFactor).
    ///    Для защиты сверхкоротких слов (например "и", "в") минимальный допуск равен 2.5 высотам текста.
    /// </summary>
    private static bool AreTextsClose(TextElement current, TextElement other)
    {
        double baseHeight = Math.Max(current.Height, other.Height);

        // Проецируем позиции на направление текстовой строки и перпендикуляр к ней
        double rotation = current.Rotation;
        double cosA = Math.Cos(rotation);
        double sinA = Math.Sin(rotation);
        _ = (current.Position.X * cosA) + (current.Position.Y * sinA);
        _ = (other.Position.X * cosA) + (other.Position.Y * sinA);

        double curPerp = (-current.Position.X * sinA) + (current.Position.Y * cosA);
        double othPerp = (-other.Position.X * sinA) + (other.Position.Y * cosA);

        // Расстояние по вертикали (перпендикуляр к строке) — должно быть в пределах одной строки
        double dy = Math.Abs(curPerp - othPerp);
        if (dy > baseHeight * LineHeightFactor)
        {
            return false;
        }

        // Расстояние по горизонтали (вдоль строки) с использованием реальных размеров объекта
        (double currentLeft, double currentRight) = GetTextBoundsAlongAxis(current, cosA, sinA);
        (double otherLeft, double otherRight) = GetTextBoundsAlongAxis(other, cosA, sinA);

        double gap = 0;
        if (currentRight < otherLeft)
        {
            gap = otherLeft - currentRight;
        }
        else if (otherRight < currentLeft)
        {
            gap = currentLeft - otherRight;
        }

        double currentWidth = Math.Abs(currentRight - currentLeft);
        double otherWidth = Math.Abs(otherRight - otherLeft);
        double baseWidth = Math.Max(currentWidth, otherWidth);

        // Расчет допуска: 1.5 от максимальной ширины текста, как запрошено.
        // Добавлен минимальный порог (baseHeight * 2.5), чтобы не сломать объединение коротких предлогов (например, "и", "в").
        double tolerance = Math.Max(baseHeight * 2.5, baseWidth * WordWidthFactor);

        return gap <= tolerance;
    }

    private static (double Left, double Right) GetTextBoundsAlongAxis(TextElement text, double cosA, double sinA)
    {
        if (text.OriginalEntity is MText mText)
        {
            double parallelCenter = (mText.Location.X * cosA) + (mText.Location.Y * sinA);
            double width = mText.ActualWidth;

            return mText.Attachment switch
            {
                AttachmentPoint.TopLeft or AttachmentPoint.MiddleLeft or AttachmentPoint.BottomLeft =>
                    (parallelCenter, parallelCenter + width),

                AttachmentPoint.TopCenter or AttachmentPoint.MiddleCenter or AttachmentPoint.BottomCenter =>
                    (parallelCenter - (width * 0.5), parallelCenter + (width * 0.5)),

                AttachmentPoint.TopRight or AttachmentPoint.MiddleRight or AttachmentPoint.BottomRight =>
                    (parallelCenter - width, parallelCenter),

                _ => (parallelCenter, parallelCenter + width)
            };
        }
        else if (text.OriginalEntity is DBText dbText)
        {
            if (dbText.Bounds.HasValue)
            {
                Extents3d ext = dbText.Bounds.Value;
                double minP = double.MaxValue;
                double maxP = double.MinValue;
                Point3d[] corners = [
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0)
                ];

                foreach (Point3d pt in corners)
                {
                    double p = (pt.X * cosA) + (pt.Y * sinA);
                    if (p < minP)
                    {
                        minP = p;
                    }

                    if (p > maxP)
                    {
                        maxP = p;
                    }
                }
                return (minP, maxP);
            }
        }

        double parallel = (text.Position.X * cosA) + (text.Position.Y * sinA);
        return (parallel, parallel + EstimateTextWidth(text));
    }

    private static double EstimateTextWidth(TextElement text)
    {
        int length = string.IsNullOrEmpty(text.TextString) ? 1 : text.TextString.Length;
        return text.Height * length * 0.8; // Увеличен коэффициент с 0.6 до 0.8 для более щедрой оценки ширины
    }

    /// <summary>
    /// Экранирует спецсимволы MText в строке, полученной из DBText.TextString,
    /// чтобы они не интерпретировались как MText-команды форматирования.
    /// </summary>
    private static string EscapeMTextContent(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Экранируем обратный слэш первым, иначе последующие замены его задвоят
        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("\n", "\\P")
            .Replace("\r", "");
    }
}

public sealed record TextElement(
    ObjectId ObjectId,
    string TextString,
    Point3d Position,
    ObjectId TextStyleId,
    double Height,
    double Rotation,
    Vector3d Normal,
    string Layer,
    Color Color,
    Entity OriginalEntity
);
