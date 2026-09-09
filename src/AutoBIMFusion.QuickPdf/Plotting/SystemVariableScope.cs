using Autodesk.AutoCAD.ApplicationServices.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.QuickPdf.Plotting;

internal sealed class SystemVariableScope : IDisposable
{
    private readonly List<(string Name, object? Previous)> _previous = [];
    private bool _disposed;

    public SystemVariableScope(params (string Name, object Value)[] variables)
    {
        foreach ((string name, object value) in variables)
        {
            _previous.Add((name, Application.GetSystemVariable(name)));
            Application.SetSystemVariable(name, value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = _previous.Count - 1; i >= 0; i--)
        {
            (string name, object? previous) = _previous[i];
            try
            {
                if (previous is not null)
                {
                    Application.SetSystemVariable(name, previous);
                }
            }
            catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or ArgumentException)
            {
                // Restore is best-effort.
            }
        }
    }
}
