using Microsoft.Win32;
using System.Diagnostics;
using Registry = Autodesk.AutoCAD.Runtime.Registry;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

internal static class PluginRegister
{
    public static void Register()
    {
        try
        {
            // Get the AutoCAD Applications key
            string sProdKey = HostApplicationServices.Current.UserRegistryProductRootKey;
            string sAppName = Generic.GetExtensionDLLName();
            Autodesk.AutoCAD.Runtime.RegistryKey regAcadProdKey = Registry.CurrentUser.OpenSubKey(sProdKey);
            Autodesk.AutoCAD.Runtime.RegistryKey regAcadAppKey = regAcadProdKey.OpenSubKey("Applications", true);

            // Check to see if the "MyApp" key exists
            string[] subKeys = regAcadAppKey.GetSubKeyNames();
            foreach (string subKey in subKeys)
            {
                // If the application is already registered, exit
                if (subKey.Equals(sAppName))
                {
                    Generic.WriteMessage($"{sAppName} est déja enregistrée");
                    regAcadAppKey.Close();
                    return;
                }
            }

            // Get the location of this module
            string sAssemblyPath = Generic.GetExtensionDLLLocation();

            // Register the application
            Autodesk.AutoCAD.Runtime.RegistryKey regAppAddInKey = regAcadAppKey.CreateSubKey(sAppName);
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
            string sProdKey = HostApplicationServices.Current.UserRegistryProductRootKey;
            string sAppName = Generic.GetExtensionDLLName();

            Autodesk.AutoCAD.Runtime.RegistryKey regAcadProdKey = Registry.CurrentUser.OpenSubKey(sProdKey);
            Autodesk.AutoCAD.Runtime.RegistryKey regAcadAppKey = regAcadProdKey.OpenSubKey("Applications", true);

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
