using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.AcadSupport;

/// <summary>
/// Временно подавляет предупреждения и диалоги AutoCAD
/// для операций экспорта и сохранения.
/// </summary>
internal sealed class AcadWarningSuppressScope : IDisposable
{
    private readonly ManagedSystemVariable _fileDia = new("FILEDIA", 0);
    private readonly ManagedSystemVariable _cmdDia = new("CMDDIA", 0);
    private readonly ManagedSystemVariable _expert = new("EXPERT", 5);
    private readonly ManagedSystemVariable _proxyNotice = new("PROXYNOTICE", 0);
    private readonly ManagedSystemVariable _layerEval = new("LAYEREVAL", 0);
    private readonly ManagedSystemVariable _layerNotify = new("LAYERNOTIFY", 0);

    public void Dispose()
    {
        _layerNotify.Dispose();
        _layerEval.Dispose();
        _proxyNotice.Dispose();
        _expert.Dispose();
        _cmdDia.Dispose();
        _fileDia.Dispose();
    }
}

/// <summary>
/// Подготавливает документ к редактированию листа: TILEMODE=0 (PaperSpace активен)
/// и сохранение предыдущих CTAB/CVPORT/TILEMODE для восстановления при Dispose.
/// </summary>
internal sealed class LayoutEditScope : IDisposable
{
    private readonly ManagedSystemVariable _tileMode = new("TILEMODE", 0);
    private readonly ManagedSystemVariable _cTab;
    private readonly ManagedSystemVariable _cvPort;

    public LayoutEditScope()
    {
        _cTab = new ManagedSystemVariable("CTAB", AcadApp.GetSystemVariable("CTAB"));
        _cvPort = new ManagedSystemVariable("CVPORT", AcadApp.GetSystemVariable("CVPORT"));
    }

    public void Dispose()
    {
        _cvPort.Dispose();
        _cTab.Dispose();
        _tileMode.Dispose();
    }
}

/// <summary>
/// Автоматизирует сохранение, изменение и восстановление
/// системной переменной AutoCAD.
/// </summary>
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
