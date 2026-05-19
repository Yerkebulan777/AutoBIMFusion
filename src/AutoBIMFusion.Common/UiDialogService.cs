using System.Diagnostics;
using System.Reflection;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Common;

public static class UiDialogService
{
    public static bool TrySelectFolder(string description, out string folderPath)
    {
        folderPath = string.Empty;

        Type? dialogType = ResolveWinFormsType("System.Windows.Forms.FolderBrowserDialog");

        if (dialogType is null)
        {
            ShowMessage("Диалог выбора папки недоступен в текущем режиме AutoCAD.", "MERGEDWG");
            return false;
        }

        using IDisposable dialog = (IDisposable)Activator.CreateInstance(dialogType)!;

        dialogType.GetProperty("Description")?.SetValue(dialog, description);
        dialogType.GetProperty("RootFolder")?.SetValue(dialog, Environment.SpecialFolder.Desktop);
        dialogType.GetProperty("ShowNewFolderButton")?.SetValue(dialog, false);

        var result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
        if (!string.Equals(result?.ToString(), "OK", StringComparison.Ordinal))
        {
            ShowMessage("Отменено пользователем.", "MERGEDWG");
            return false;
        }

        folderPath = (string?)dialogType.GetProperty("SelectedPath")?.GetValue(dialog) ?? string.Empty;

        return !string.IsNullOrWhiteSpace(folderPath);
    }

    public static void ShowMessage(string message, string caption)
    {
        try
        {
            AcadApp.ShowAlertDialog($"{caption}\n\n{message}");
        }
        finally
        {
            Debug.WriteLine($"{caption}\n\n{message}");
        }
    }

    private static Type? ResolveWinFormsType(string typeName)
    {
        Type? type = Type.GetType($"{typeName}, System.Windows.Forms", false);

        if (type is not null)
        {
            return type;
        }

        try
        {
            Assembly assembly = Assembly.Load("System.Windows.Forms");
            return assembly.GetType(typeName, false);
        }
        catch
        {
            return null;
        }
    }
}
