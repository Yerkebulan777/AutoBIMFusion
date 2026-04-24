using AutoBIMFusion.Application.Ribbon;
using Autodesk.AutoCAD.ApplicationServices;
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
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        RibbonBuilder.CreateTab();

        App.Idle -= OnIdle;
    }



}
