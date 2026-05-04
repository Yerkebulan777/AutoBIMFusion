using System;
using System.Collections.Generic;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.AcadSupport;

internal sealed class AcadWarningSuppressScope : IDisposable
{
    private readonly List<ManagedSystemVariable> _variables = new();

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
        _variables.Add(new ManagedSystemVariable(name, value));
    }

    public void Dispose()
    {
        for (int i = _variables.Count - 1; i >= 0; i--)
        {
            _variables[i].Dispose();
        }
    }
}

internal sealed class AcadUnitScalingOverrideScope : IDisposable
{
    private readonly List<ManagedSystemVariable> _variables = new();

    public AcadUnitScalingOverrideScope()
    {
        Set("INSUNITSDEFSOURCE", 4);
        Set("INSUNITSDEFTARGET", 4);
    }

    private void Set(string name, object value)
    {
        _variables.Add(new ManagedSystemVariable(name, value));
    }

    public void Dispose()
    {
        for (int i = _variables.Count - 1; i >= 0; i--)
        {
            _variables[i].Dispose();
        }
    }
}

internal sealed class ManagedSystemVariable : IDisposable
{
    private readonly string _name;
    private readonly object? _oldValue;
    private readonly bool _isSet;

    public ManagedSystemVariable(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));
        }

        _name = name;
        try
        {
            _oldValue = AcadApp.GetSystemVariable(name);
            AcadApp.SetSystemVariable(name, value);
            _isSet = true;
        }
        catch
        {
            // Ignore if the variable does not exist, is read-only, or throws an exception.
            _isSet = false;
        }
    }

    public void Dispose()
    {
        if (_isSet)
        {
            try
            {
                AcadApp.SetSystemVariable(_name, _oldValue);
            }
            catch
            {
                // Ignore errors during restore to ensure other variables still get restored.
            }
        }
    }
}
