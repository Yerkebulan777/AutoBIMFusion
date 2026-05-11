using System.Diagnostics;
using Microsoft.Win32;
using Registry = Autodesk.AutoCAD.Runtime.Registry;

namespace SioForgeCAD.Commun;

internal static class PluginRegister
{
    public static void Register()
    {
        try
        {
            // Get the AutoCAD Applications key
            var sProdKey = HostApplicationServices.Current.UserRegistryProductRootKey;
            var sAppName = Generic.GetExtensionDLLName();
            var regAcadProdKey = Registry.CurrentUser.OpenSubKey(sProdKey);
            var regAcadAppKey = regAcadProdKey.OpenSubKey("Applications", true);

            // Check to see if the "MyApp" key exists
            string[] subKeys = regAcadAppKey.GetSubKeyNames();
            foreach (var subKey in subKeys)
                // If the application is already registered, exit
                if (subKey.Equals(sAppName))
                {
                    Generic.WriteMessage($"{sAppName} est déja enregistrée");
                    regAcadAppKey.Close();
                    return;
                }

            // Get the location of this module
            var sAssemblyPath = Generic.GetExtensionDLLLocation();

            // Register the application
            var regAppAddInKey = regAcadAppKey.CreateSubKey(sAppName);
            regAppAddInKey.SetValue("DESCRIPTION", sAppName, RegistryValueKind.String);
            regAppAddInKey.SetValue("LOADCTRLS", 14, RegistryValueKind.DWord);
            regAppAddInKey.SetValue("LOADER", sAssemblyPath, RegistryValueKind.String);
            regAppAddInKey.SetValue("MANAGED", 1, RegistryValueKind.DWord);
            regAcadAppKey.Close();
            Generic.WriteMessage($"{sAppName} à été enregistrée avec succès");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de l'inscription de l'application : {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            // Get the AutoCAD Applications key
            var sProdKey = HostApplicationServices.Current.UserRegistryProductRootKey;
            var sAppName = Generic.GetExtensionDLLName();

            var regAcadProdKey = Registry.CurrentUser.OpenSubKey(sProdKey);
            var regAcadAppKey = regAcadProdKey.OpenSubKey("Applications", true);

            // Delete the key for the application
            regAcadAppKey.DeleteSubKeyTree(sAppName);
            regAcadAppKey.Close();
            Generic.WriteMessage($"{sAppName} ne se chargera désormais plus au démarage d'AutoCAD");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de la désinscription de l'application : {ex.Message}");
        }
    }
}
