using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.GraphicsSystem;
using System.Drawing;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoBIMFusion.Common.Extensions;

public static class LayoutsExtensions
{
    public static void CloneLayout(this Layout sourceLayout, string newLayoutName)
    {
        Document doc = AcAp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord sourceBtr = (BlockTableRecord)trx.GetObject(sourceLayout.BlockTableRecordId, OpenMode.ForRead);
        LayoutManager lm = LayoutManager.Current;

        ObjectId newLayoutId = lm.CreateLayout(newLayoutName);
        Layout newLayout = (Layout)trx.GetObject(newLayoutId, OpenMode.ForWrite);
        BlockTableRecord newBtr = (BlockTableRecord)trx.GetObject(newLayout.BlockTableRecordId, OpenMode.ForWrite);

        IdMapping mapping = [];
        db.DeepCloneObjects(sourceBtr.Cast<ObjectId>().ToObjectIdCollection(), newBtr.ObjectId, mapping, false);

        // 3. Copie les réglages de tracé
        newLayout.CopyFrom(sourceLayout);
        lm.CurrentLayout = newLayoutName;

        trx.Commit();
    }

    public static Bitmap GetLayoutSnapshot(this Layout lay, Extents3d ext, int width, int height)
    {
        Document doc = AcAp.DocumentManager.MdiActiveDocument;
        Manager gsm = doc.GraphicsManager;

        KernelDescriptor descriptor = new();
        descriptor.addRequirement(UniqueString.Intern("3D Drawing"));
        GraphicsKernel kernel = Manager.AcquireGraphicsKernel(descriptor);

        using Transaction trx = lay.Database.TransactionManager.StartOpenCloseTransaction();
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);

        using View view = new();
        double w = ext.MaxPoint.X - ext.MinPoint.X;
        double h = ext.MaxPoint.Y - ext.MinPoint.Y;
        Point3d center = new(ext.MinPoint.X + (w / 2), ext.MinPoint.Y + (h / 2), 0);

        // Position de la caméra (Z+1) pour ne pas être "dans" le dessin
        Point3d eyePosition = new(center.X, center.Y, center.Z + 1.0);

        // Cadrage de la vue
        view.SetView(eyePosition, center, Vector3d.YAxis, w, h);

        using Device dev = gsm.CreateAutoCADOffScreenDevice(kernel);
        dev.OnSize(new Size(width, height));
        dev.BackgroundColor = Color.White;
        _ = dev.Add(view);

        using Model model = gsm.CreateAutoCADModel(kernel);
        _ = view.Add(btr, model);
        dev.Update();
        view.Update();

        return view.GetSnapshot(new Rectangle(0, 0, width, height));
    }

    public static Bitmap RenderLayoutSnapshot(this Layout layout)
    {
        Database db = Generic.GetDatabase();
        int bmpW = 100;
        int bmpH = 100;
        using (Transaction transaction = db.TransactionManager.StartTransaction())
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

                double realW = ext.MaxPoint.X - ext.MinPoint.X;
                double realH = ext.MaxPoint.Y - ext.MinPoint.Y;

                if (realW <= 0.001 || realH <= 0.001)
                {
                    return null;
                }

                double ratio = Min(512.0 / realW, 512.0 / realH);

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
