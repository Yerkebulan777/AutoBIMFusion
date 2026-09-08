using System.Reflection;
using AutoBIMFusion.Common;

// Run in a fresh process with no static WinForms reference or prior UI initialization.
// Only the AutoCAD alert endpoint is stubbed; the production resolver is exercised.
if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Windows.Forms"))
    throw new InvalidOperationException("WinForms was preloaded; cold-start regression is not being tested.");

MethodInfo resolve = typeof(UiDialogService).GetMethod("ResolveWinFormsType", BindingFlags.NonPublic | BindingFlags.Static)!;
Type? dialogType = (Type?)resolve.Invoke(null, new object[] { "System.Windows.Forms.FolderBrowserDialog" });
if (dialogType is null)
    throw new InvalidOperationException("Folder dialog unavailable: production resolver returned null.");
if (dialogType.GetMethod("ShowDialog", Type.EmptyTypes) is null)
    throw new InvalidOperationException("Folder dialog has no ShowDialog method.");
if (resolve.Invoke(null, new object[] { "System.Windows.Forms.NonexistentDialog" }) is not null)
    throw new InvalidOperationException("Unknown dialog type should return null.");
Console.WriteLine("PASS: cold-start folder dialog resolution and missing type handling.");

namespace Autodesk.AutoCAD.ApplicationServices.Core
{
    internal static class Application
    {
        public static void ShowAlertDialog(string message) => throw new InvalidOperationException(message);
    }
}
