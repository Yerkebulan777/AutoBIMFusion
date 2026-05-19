using System.Diagnostics;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.AcadSupport;

/// <summary>
///     Подавляет диалоги и предупреждения AutoCAD на время операции слияния.
///     Устанавливает FILEDIA=0, CMDDIA=0, EXPERT=5, PROXYNOTICE=0,
///     LAYEREVAL=0, LAYERNOTIFY=0, LAYOUTREGENCTL=0, VTENABLE=0.
///     Восстанавливает исходные значения при вызове Dispose.
/// </summary>
public sealed class AcadWarningSuppressScope : IDisposable
{
    private static readonly (string Name, object SuppressedValue)[] Variables =
    [
        ("FILEDIA", 0),
        ("CMDDIA", 0),
        ("EXPERT", 5),
        ("PROXYNOTICE", 0),
        ("LAYEREVAL", 0),
        ("LAYERNOTIFY", 0),
        ("LAYOUTREGENCTL", 0),
        ("VTENABLE", 0)
    ];

    private readonly Dictionary<string, object> _originalValues = new();

    public AcadWarningSuppressScope()
    {
        foreach ((string? name, object? suppressedValue) in Variables)
        {
            try
            {
                _originalValues[name] = AcadApp.GetSystemVariable(name);
                AcadApp.SetSystemVariable(name, suppressedValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось установить системную переменную {name}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        foreach ((string? name, object? originalValue) in _originalValues)
        {
            try
            {
                AcadApp.SetSystemVariable(name, originalValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось восстановить системную переменную {name}: {ex.Message}");
            }
        }
    }
}
