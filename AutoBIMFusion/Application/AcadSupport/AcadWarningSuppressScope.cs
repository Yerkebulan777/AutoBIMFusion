using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.AcadSupport;

internal sealed class AcadWarningSuppressScope : IDisposable
{
    private readonly ManagedSystemVariable _fileDia = new("FILEDIA", 0);
    private readonly ManagedSystemVariable _cmdDia = new("CMDDIA", 0);
    private readonly ManagedSystemVariable _expert = new("EXPERT", 5);
    private readonly ManagedSystemVariable _proxyNotice = new("PROXYNOTICE", 0);
    private readonly ManagedSystemVariable _layerEval = new("LAYEREVAL", 0);
    private readonly ManagedSystemVariable _layerNotify = new("LAYERNOTIFY", 0);
    private readonly ManagedSystemVariable _layoutRegenCtl = new("LAYOUTREGENCTL", 0);
    private readonly ManagedSystemVariable _vtEnable = new("VTENABLE", 0);

    public void Dispose()
    {
        _vtEnable.Dispose();
        _layoutRegenCtl.Dispose();
        _layerNotify.Dispose();
        _layerEval.Dispose();
        _proxyNotice.Dispose();
        _expert.Dispose();
        _cmdDia.Dispose();
        _fileDia.Dispose();
    }
}

// Устанавливает TILEMODE=0, сохраняет CTAB/CVPORT/TILEMODE для восстановления при Dispose.
// ПРИМЕЧАНИЕ: Класс LayoutEditScope был удалён как неиспользуемый (dead code).
// Если потребуется работа с layout-контекстом, используйте прямое управление системными переменными.

internal sealed class ManagedSystemVariable : IDisposable
{
    private readonly string _name;
    private readonly object _oldValue;

    public ManagedSystemVariable(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Value cannot be null or whitespace.",
                nameof(name));
        }

        _name = name;
        _oldValue = AcadApp.GetSystemVariable(name);
        AcadApp.SetSystemVariable(name, value);
    }

    public void Dispose()
    {
        AcadApp.SetSystemVariable(_name, _oldValue);
    }
}
