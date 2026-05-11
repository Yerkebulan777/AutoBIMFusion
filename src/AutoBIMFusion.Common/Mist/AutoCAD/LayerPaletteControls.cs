using System.Reflection;

namespace SioForgeCAD.Commun;

public static class LayerPaletteControls
{
    public static (object? LayerGrid, object? LayerManager) GetLayerPaletteControls()
    {
        var paletteHostType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Autodesk.AutoCAD.LayerManager.PaletteHost", false))
            .FirstOrDefault(type => type != null);
        var field = paletteHostType?.GetField("layerManager_", BindingFlags.Static | BindingFlags.NonPublic);
        if (field != null)
        {
            var rslt = field.GetValue(null);
            if (rslt != null)
            {
                var method = rslt.GetType().GetMethod("FindControlByTypeName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var layerGridControl = method?.Invoke(rslt, new object[] { "LayerGrid" });
                if (layerGridControl != null) return (layerGridControl, rslt);
            }
        }

        return (null, null);
    }
}
