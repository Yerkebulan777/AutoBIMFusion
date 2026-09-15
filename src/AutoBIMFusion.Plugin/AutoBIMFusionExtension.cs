using System.Runtime.Versioning;
using AutoBIMFusion.Plugin.Ribbon;
using Autodesk.Windows;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Plugin;

[SupportedOSPlatform("Windows")]
public sealed class AutoBIMFusionExtension : IExtensionApplication
{
    public void Initialize()
    {
        App.Idle += OnIdle;
    }

    public void Terminate()
    {
        App.Idle -= OnIdle;
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        var doc = App.DocumentManager.MdiActiveDocument;

        if (doc != null && ComponentManager.Ribbon != null)
        {
            RibbonBuilder.CreateTab();

            App.Idle -= OnIdle;
        }
    }
}
