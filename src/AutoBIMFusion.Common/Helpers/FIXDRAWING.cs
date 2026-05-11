using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;

namespace AutoBIMFusion.Common.Helpers;

public static class FIXDRAWING
{
    public static void Fix()
    {
        var ed = Generic.GetEditor();
        var db = Generic.GetDatabase();

        // Configure plot stamp settings
        Generic.Command("_-PLOTSTAMP", "_LOG", "_NO", "plot.log", "");

        // Audit and correct drawing errors
        Generic.Command("_AUDIT", "_YES");

        // Save current layout
        var lm = LayoutManager.Current;
        var SavedCurrentLayout = lm.CurrentLayout;

        // Set ModelSpace settings
        Generic.WriteMessage("Modifying variables in model space...");
        lm.SetCurrentLayoutId(ed.GetModelLayout().ObjectId);
        ApplyPreferedSystemVariable();

        // Set insertion base point to origin
        Generic.Command("_BASE", new Point3d(0, 0, 0));
        Generic.Command("_SNAP", "_OFF");
        // Set drawing units to meters
        Generic.Command("_INSUNITS", 6);
        Generic.Command("_-UNITS", 2, 4, 1, 4, 0, "_NO");
        Generic.Command("_INSBASE", Point3d.Origin);

        db.SetAnnotativeScale("1:1", 1, 1);

        // Apply settings to all layouts
        foreach (var layout in ed.GetAllLayout())
        {
            Generic.WriteMessage($"Modifying variables on layout \"{layout.LayoutName}\"...");
            lm.CurrentLayout = layout.LayoutName;
            ApplyPreferedSystemVariable();
        }

        // Restore previous layout
        lm.CurrentLayout = SavedCurrentLayout;
    }

    /// <summary>
    ///     Applies a predefined set of system variable settings to ensure consistent drawing behavior and appearance.
    /// </summary>
    public static void ApplyPreferedSystemVariable()
    {
        // UCS settings
        Generic.SetSystemVariable("UCSFOLLOW", 0); // Disable plan view generation on UCS change
        Generic.SetSystemVariable("UCSDETECT", 0); // Disable automatic UCS detection
        Generic.SetSystemVariable("UCSICON", 3); // Show UCS icon at origin when active

        // Interface and input settings
        Generic.SetSystemVariable("ROLLOVERTIPS", 0); // Disable rollover tooltips
        Generic.SetSystemVariable("QPMODE", -1); // Quick properties mode
        Generic.SetSystemVariable("FILEDIA", 1); // Enable file navigation dialog
        Generic.SetSystemVariable("DYNMODE", 3); // Enable pointer and dimensional input

        // Object transparency and display
        Generic.SetSystemVariable("CETRANSPARENCY", -1); // Set transparency to ByLayer
        Generic.SetSystemVariable("FILLMODE", 1); // Enable hatches and fills
        Generic.SetSystemVariable("XCOMPAREENABLE", 0); // Disable external reference comparison
        Generic.SetSystemVariable("LINESMOOTHING", 1); // Enable line smoothing
        Generic.SetSystemVariable("WIPEOUTFRAME", 2); // Display wipeout frames (not plotted)

        // Hatch and fill settings
        Generic.SetSystemVariable("HPASSOC", 1); // Make hatches associative with boundaries
        Generic.SetSystemVariable("HPLAYER", "."); // Use current layer for hatches
        Generic.SetSystemVariable("HPSCALE", 1); // Hatch pattern scale

        // Draw order and sorting
        Generic.SetSystemVariable("DRAWORDERCTL", 3); // Control display order of overlapping objects
        Generic.SetSystemVariable("SORTENTS", 127); // Enable object sorting for draw order

        // Linetype settings
        Generic.SetSystemVariable("PSLTSCALE", 0); // Disable linetype scaling in paper space
        Generic.SetSystemVariable("LTSCALE", 1); // Global linetype scale factor
        Generic.SetSystemVariable("CELTSCALE", 1); // Current linetype scale factor
        Generic.SetSystemVariable("MSLTSCALE", 1); // Model space linetype scale by annotation
        Generic.SetSystemVariable("LINEFADING", 1); // Enable line fading

        // Units and measurement settings
        Generic.SetSystemVariable("MEASUREMENT", 1); // Use metric hatch patterns and linetypes
        Generic.SetSystemVariable("MEASUREINIT", 1); // Use metric defaults for new drawings
        Generic.SetSystemVariable("INSUNITS", 6); // Auto-scale inserted blocks to meters

        // Selection and editing
        Generic.SetSystemVariable("PICKADD", 2); // Add selected objects to selection set
        Generic.SetSystemVariable("PICKAUTO", 5); // Auto-select mode
        Generic.SetSystemVariable("TRIMEXTENDMODE", 0); // Use standard TRIM/EXTEND operation
        Generic.SetSystemVariable("EDGEMODE", 1); // Edge mode for TRIM/EXTEND

        // External reference and scale settings
        Generic.SetSystemVariable("HIDEXREFSCALES", 1); // Hide xref scales from annotation list
        Generic.SetSystemVariable("VISRETAIN", 1); // Retain xref-dependent layer properties
        Generic.SetSystemVariable("XREFNOTIFY", 2); // Notify about updated or missing xrefs
        Generic.SetSystemVariable("INDEXCTL", 0); // Disable drawing file indexing

        // Lock and PDF settings
        Generic.SetSystemVariable("LOCKUI", 0); // Enable UI element repositioning
        Generic.SetSystemVariable("EPDFSHX", 0); // PDF shape settings
        Generic.SetSystemVariable("PDFSHX", 0); // PDF shape settings
    }
}
