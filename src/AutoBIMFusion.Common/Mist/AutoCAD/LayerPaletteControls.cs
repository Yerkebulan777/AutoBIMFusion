using System.Reflection;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public static class LayerPaletteControls
{
    public static (object? LayerGrid, object? LayerManager) GetLayerPaletteControls()
    {
        Type? paletteHostType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Autodesk.AutoCAD.LayerManager.PaletteHost", false))
            .FirstOrDefault(type => type != null);
        FieldInfo? field = paletteHostType?.GetField("layerManager_", BindingFlags.Static | BindingFlags.NonPublic);
        if (field != null)
        {
            object? rslt = field.GetValue(null);
            if (rslt != null)
            {
                MethodInfo? method = rslt.GetType().GetMethod("FindControlByTypeName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? layerGridControl = method?.Invoke(rslt, new object[] { "LayerGrid" });
                if (layerGridControl != null)
                {
                    return (layerGridControl, rslt);
                }
            }
        }

        return (null, null);
    }
}
