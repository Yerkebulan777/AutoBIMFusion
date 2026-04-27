using Autodesk.Windows;
using System.Runtime.Versioning;
using System.Windows.Input;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Ribbon;

/// <summary>
/// Обработчик команд Ribbon кнопок AutoBIMFusion.
/// </summary>
[SupportedOSPlatform ("windows")]
internal sealed class ButtonCommandHandler : ICommand
{
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        string? command = parameter switch
        {
            RibbonButton { CommandParameter: string cmd } => cmd,
            string cmd => cmd,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        App.DocumentManager.MdiActiveDocument?.SendStringToExecute(command, true, false, false);
    }
}
