using System.Reflection;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Utils;

internal static class UiDialogService
{
    internal static bool TrySelectFolder(string description, out string folderPath)
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

        object? result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
        if (!string.Equals(result?.ToString(), "OK", StringComparison.Ordinal))
        {
            ShowMessage("Отменено пользователем.", "MERGEDWG");
            return false;
        }

        folderPath = (string?)dialogType.GetProperty("SelectedPath")?.GetValue(dialog) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(folderPath);
    }

    internal static void ShowMessage(string message, string caption)
    {
        try
        {
            AcadApp.ShowAlertDialog($"{caption}\n\n{message}");
        }
        catch
        {
            // Core Console and invalid UI contexts can reject dialogs; command logs carry the details.
        }
    }

    private static Type? ResolveWinFormsType(string typeName)
    {
        Type? type = Type.GetType($"{typeName}, System.Windows.Forms", throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        try
        {
            Assembly assembly = Assembly.Load("System.Windows.Forms");
            return assembly.GetType(typeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }
}
