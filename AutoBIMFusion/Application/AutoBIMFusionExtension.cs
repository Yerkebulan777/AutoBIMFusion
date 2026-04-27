using AutoBIMFusion.Application.Ribbon;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using System.Runtime.Versioning;

using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application;

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
        Document? doc = App.DocumentManager.MdiActiveDocument;

        if (doc != null && ComponentManager.Ribbon != null)
        {
            RibbonBuilder.CreateTab();

            App.Idle -= OnIdle;
        }
    }
}
