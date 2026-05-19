using System.Diagnostics;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.AcadSupport;

/// <summary>
///     Подавляет диалоги и предупреждения AutoCAD на время операции слияния.
///     Устанавливает FILEDIA=0, CMDDIA=0, EXPERT=5, PROXYNOTICE=0,
///     LAYEREVAL=0, LAYERNOTIFY=0, LAYOUTREGENCTL=0, VTENABLE=0.
/// </summary>
public sealed class AcadWarningSuppressScope : IDisposable
{
    private static readonly (string Name, object SuppressedValue, object DefaultValue)[] Variables =
    [
        ("FILEDIA", 0, 1),
        ("CMDDIA", 0, 1),
        ("EXPERT", 5, 0),
        ("PROXYNOTICE", 0, 1),
        ("LAYEREVAL", 0, 0),
        ("LAYERNOTIFY", 0, 0),
        ("LAYOUTREGENCTL", 0, 2),
        ("VTENABLE", 0, 3)
    ];

    public AcadWarningSuppressScope()
    {
        foreach ((string? name, object? suppressedValue, object _) in Variables)
        {
            Set(name, suppressedValue);
        }
    }

    public void Dispose()
    {
        ResetToDefaultValues();
    }

    public static void ResetToDefaultValues()
    {
        foreach ((string? name, object _, object? defaultValue) in Variables)
        {
            Set(name, defaultValue);
        }
    }

    private static void Set(string name, object value)
    {
        try
        {
            _ = AcadApp.GetSystemVariable(name);
            AcadApp.SetSystemVariable(name, value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Не удалось установить системную переменную {name}: {ex.Message}");
        }
    }
}
