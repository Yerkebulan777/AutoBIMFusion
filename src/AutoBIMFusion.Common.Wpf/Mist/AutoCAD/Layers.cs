using System.Reflection;
using System.Windows.Forms;

namespace SioForgeCAD.Commun
{
    public static class LayerPaletteControls
    {
        public static (DataGridView LayerGrid, object LayerManager) GetLayerPaletteControls()
        {
            Type paletteHostType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Autodesk.AutoCAD.LayerManager.PaletteHost", throwOnError: false))
                .FirstOrDefault(type => type != null);
            FieldInfo field = paletteHostType?.GetField("layerManager_", BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null)
            {
                object rslt = field.GetValue(null);
                if (rslt != null)
                {
                    MethodInfo method = rslt.GetType().GetMethod("FindControlByTypeName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method?.Invoke(rslt, new object[] { "LayerGrid" }) is DataGridView LayerGridControl)//Autodesk.AutoCAD.LayerManager.LayerGrid
                    {
                        return (LayerGridControl, rslt);
                    }
                }
            }

            return (null, null);
        }
    }
}
