using System.Diagnostics;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Common.AcadSupport;

/// <summary>
///     Подавляет диалоги и предупреждения AutoCAD на время операции слияния.
///     При Dispose восстанавливает заводские значения по умолчанию.
/// </summary>
public sealed class AcadWarningSuppressScope : IDisposable
{
    private static readonly (string Name, object SuppressedValue, object DefaultValue)[] Variables =
    [
        ("FILEDIA",       0, 1),
        ("CMDDIA",        0, 1),
        ("EXPERT",        5, 0),
        ("PROXYNOTICE",   0, 1),
        ("LAYEREVAL",     0, 1),
        ("LAYERNOTIFY",   0, 1),
        ("LAYOUTREGENCTL",0, 2),
        ("VTENABLE",      0, 3)
    ];

    public AcadWarningSuppressScope()
    {
        foreach ((string name, object suppressedValue, object _) in Variables)
        {
            try
            {
                AcadApp.SetSystemVariable(name, suppressedValue);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Не удалось установить системную переменную {name}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        foreach ((string name, object _, object defaultValue) in Variables)
        {
            try
            {
                AcadApp.SetSystemVariable(name, defaultValue);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Не удалось восстановить системную переменную {name}: {ex.Message}");
            }
        }
    }
}
