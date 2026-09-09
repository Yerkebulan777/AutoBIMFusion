using AutoBIMFusion.Merge.Combine;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Serilog;
using Exception = System.Exception;

[assembly: CommandClass(typeof(AutoBIMFusion.Raster.HostTests.RasterTests))]

namespace AutoBIMFusion.Raster.HostTests;

public class RasterTests
{
    [CommandMethod("ABF_RASTER_TEST", CommandFlags.Session)]
    public void Run()
    {
        var root = Environment.GetEnvironmentVariable("ABF_RASTER_TEST_ROOT")!;
        var results = new List<string>();
        try
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument())
            using (var log = new LoggerConfiguration().WriteTo.File(Path.Combine(root, "raster.log")).CreateLogger())
            {
                var db = doc.Database;
                var sourceDir = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
                var outputDir = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
                var imagePath = Path.Combine(sourceDir, "image.png");
                File.WriteAllBytes(imagePath, Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1sAAAAASUVORK5CYII="));
                ObjectId defId;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    RasterImageDef.CreateImageDictionary(db);
                    var dict = (DBDictionary)tr.GetObject(RasterImageDef.GetImageDictionary(db), OpenMode.ForWrite);
                    var def = new RasterImageDef { SourceFileName = imagePath };
                    def.Load();
                    defId = dict.SetAt("fixture", def);
                    tr.AddNewlyCreatedDBObject(def, true);
                    var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var image = new RasterImage
                    {
                        ImageDefId = defId,
                        Orientation = new CoordinateSystem3d(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis)
                    };
                    model.AppendEntity(image);
                    tr.AddNewlyCreatedDBObject(image, true);
                    RasterImage.EnableReactors(true);
                    image.AssociateRasterDef(def);
                    tr.Commit();
                }

                var output = Path.Combine(outputDir, "merged.dwg");
                RasterImagePathFixer.CopyImagesToTargetFolder(db, output, log, sourceDir);
                Check(db, defId, "after copy", false, results);
                db.SaveAs(output, DwgVersion.Current);
                // In the affected host this remains the template filename after SaveAs.
                results.Add("Database filename: " + db.Filename);
                RasterImagePathFixer.ConvertPathsToRelative(db, output, log);
                Check(db, defId, "after relative", true, results);
                // The already-relative branch must preserve the loaded state too.
                RasterImagePathFixer.ConvertPathsToRelative(db, output, log);
                Check(db, defId, "after repeat", true, results);
                db.SaveAs(output, DwgVersion.Current);
                using var reopened = new Database(false, true);
                reopened.ReadDwgFile(output, FileOpenMode.OpenForReadAndAllShare, false, null);
                using var read = reopened.TransactionManager.StartTransaction();
                var savedDict = (DBDictionary)read.GetObject(RasterImageDef.GetImageDictionary(reopened), OpenMode.ForRead);
                Check(reopened, savedDict.GetAt("fixture"), "reopened", true, results);
                read.Commit();
            }
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
        }
        File.WriteAllLines(Path.Combine(root, "result.txt"), results);
    }

    private static void Check(Database db, ObjectId id, string stage, bool relative, List<string> results)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var def = (RasterImageDef)tr.GetObject(id, OpenMode.ForRead);
        results.Add($"{(def.IsLoaded ? "PASS" : "FAIL")} {stage}: loaded={def.IsLoaded}");
        if (relative)
            results.Add($"{(def.SourceFileName == "image.png" ? "PASS" : "FAIL")} {stage}: path={def.SourceFileName}");
        results.Add($"{(def.GetEntityCount(out _) == 1 ? "PASS" : "FAIL")} {stage}: image entity retained");
        tr.Commit();
    }
}
