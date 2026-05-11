using System.Drawing;
using Autodesk.AutoCAD;
using Autodesk.AutoCAD.GraphicsSystem;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SioForgeCAD.Commun.Extensions;

public static class LayoutsExtensions
{
    public static void CloneLayout(this Layout sourceLayout, string newLayoutName)
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var sourceBtr = (BlockTableRecord)tr.GetObject(sourceLayout.BlockTableRecordId, OpenMode.ForRead);
            var lm = LayoutManager.Current;

            var newLayoutId = lm.CreateLayout(newLayoutName);
            var newLayout = (Layout)tr.GetObject(newLayoutId, OpenMode.ForWrite);
            var newBtr = (BlockTableRecord)tr.GetObject(newLayout.BlockTableRecordId, OpenMode.ForWrite);

            var mapping = new IdMapping();
            db.DeepCloneObjects(sourceBtr.Cast<ObjectId>().ToObjectIdCollection(), newBtr.ObjectId, mapping, false);

            // 3. Copie les réglages de tracé
            newLayout.CopyFrom(sourceLayout);
            lm.CurrentLayout = newLayoutName;

            tr.Commit();
        }
    }

    public static Bitmap GetLayoutSnapshot(this Layout lay, Extents3d ext, int width, int height)
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var gsm = doc.GraphicsManager;

        var descriptor = new KernelDescriptor();
        descriptor.addRequirement(UniqueString.Intern("3D Drawing"));
        var kernel = Manager.AcquireGraphicsKernel(descriptor);

        using (Transaction tr = lay.Database.TransactionManager.StartOpenCloseTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);

            using (var view = new View())
            {
                var w = ext.MaxPoint.X - ext.MinPoint.X;
                var h = ext.MaxPoint.Y - ext.MinPoint.Y;
                var center = new Point3d(ext.MinPoint.X + w / 2, ext.MinPoint.Y + h / 2, 0);

                // Position de la caméra (Z+1) pour ne pas être "dans" le dessin
                var eyePosition = new Point3d(center.X, center.Y, center.Z + 1.0);

                // Cadrage de la vue
                view.SetView(eyePosition, center, Vector3d.YAxis, w, h);

                using (var dev = gsm.CreateAutoCADOffScreenDevice(kernel))
                {
                    dev.OnSize(new Size(width, height));
                    dev.BackgroundColor = Color.White;
                    dev.Add(view);

                    using (var model = gsm.CreateAutoCADModel(kernel))
                    {
                        view.Add(btr, model);
                        dev.Update();
                        view.Update();

                        return view.GetSnapshot(new Rectangle(0, 0, width, height));
                    }
                }
            }
        }
    }

    public static Bitmap RenderLayoutSnapshot(this Layout layout)
    {
        var db = Generic.GetDatabase();
        var bmpW = 100;
        var bmpH = 100;
        using (var transaction = db.TransactionManager.StartTransaction())
        {
            try
            {
                Extents3d ext;
                if (layout.ModelType)
                {
                    db.UpdateExt(true);
                    ext = new Extents3d(db.Extmin, db.Extmax);
                }
                else
                {
                    ext = layout.Extents;
                }

                var realW = ext.MaxPoint.X - ext.MinPoint.X;
                var realH = ext.MaxPoint.Y - ext.MinPoint.Y;

                if (realW <= 0.001 || realH <= 0.001) return null;
                var ratio = Min(512.0 / realW, 512.0 / realH);

                bmpW = (int)(realW * ratio);
                bmpH = (int)(realH * ratio);
                return layout.GetLayoutSnapshot(ext, bmpW, bmpH);
            }
            catch
            {
            }
            finally
            {
                transaction.Commit();
            }
        }

        return new Bitmap(bmpW, bmpH);
    }
}
