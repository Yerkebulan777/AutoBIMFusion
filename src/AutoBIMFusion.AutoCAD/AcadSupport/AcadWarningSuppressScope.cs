using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.AutoCAD.AcadSupport;

/// <summary>
/// Подавляет диалоги и предупреждения AutoCAD на время операции слияния.
/// Устанавливает FILEDIA=0, CMDDIA=0, EXPERT=5, PROXYNOTICE=0,
/// LAYEREVAL=0, LAYERNOTIFY=0, LAYOUTREGENCTL=0, VTENABLE=0.
/// </summary>
public sealed class AcadWarningSuppressScope : IDisposable
{
    private readonly List<(string Name, object? OldValue, bool IsSet)> _vars = [];

    public AcadWarningSuppressScope()
    {
        Set("FILEDIA", 0);
        Set("CMDDIA", 0);
        Set("EXPERT", 5);
        Set("PROXYNOTICE", 0);
        Set("LAYEREVAL", 0);
        Set("LAYERNOTIFY", 0);
        Set("LAYOUTREGENCTL", 0);
        Set("VTENABLE", 0);
    }

    private void Set(string name, object value)
    {
        try
        {
            object? oldValue = AcadApp.GetSystemVariable(name);
            AcadApp.SetSystemVariable(name, value);
            _vars.Add((name, oldValue, true));
        }
        catch
        {
            // Переменная недоступна или только для чтения — пропускаем.
            _vars.Add((name, null, false));
        }
    }

    public void Dispose()
    {
        // Восстанавливаем в обратном порядке для корректного стека зависимостей.
        for (int i = _vars.Count - 1; i >= 0; i--)
        {
            (string? name, object? oldValue, bool isSet) = _vars[i];
            if (!isSet)
            {
                continue;
            }

            try { AcadApp.SetSystemVariable(name, oldValue!); }
            catch { /* Игнорируем: другие переменные должны быть восстановлены. */ }
        }
    }
}

