using System.Runtime.Versioning;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.Plugin.Ribbon;
using Autodesk.Windows;
using Serilog.Core;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Plugin;

[SupportedOSPlatform("Windows")]
public sealed class AutoBIMFusionExtension : IExtensionApplication
{
    public void Initialize()
    {
        Logger log = LoggerFactory.GetSharedLogger();
        log.Information("AutoBIMFusion loaded. Log: {LogPath}", LoggerFactory.GetCurrentLogFilePath());
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
