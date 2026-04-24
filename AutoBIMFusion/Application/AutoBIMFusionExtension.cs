using AutoBIMFusion.Application.Ribbon;
using AutoBIMFusion.Infrastructure.Logging;
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
        OperationLogger logger = new(doc.Editor);
        logger.Info("AutoBIMFusion загружен.");
        RibbonBuilder.CreateTab();

        App.Idle -= OnIdle;
    }

}

