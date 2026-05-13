using System.Drawing;
using System.Windows.Media.Imaging;
using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.Internal;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.Extensions;

public static class LayoutManagerExtensions
{
    public static BitmapSource? GetLayoutImage(this LayoutManager lm, string Name)
    {
        var db = Generic.GetDatabase();
        var Doc = Generic.GetDocument();
        if (string.IsNullOrEmpty(Name)) return null;

        BitmapSource? result = null;
        try
        {
            var bitmap = Utils.GetLayoutThumbnail(Doc, Name);
            if (bitmap == null)
            {
                var database = Doc.Database;
                if (database != null)
                {
                    try
                    {
                        using (Transaction transaction = database.TransactionManager.StartOpenCloseTransaction())
                        using (var dBObject = transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead))
                        {
                            var dBDictionary = dBObject as DBDictionary;
                            if (dBDictionary == null) return null;

                            var at = dBDictionary.GetAt(Name);
                            var layout = transaction.GetObject(at, OpenMode.ForRead) as Layout;
                            bitmap = layout?.Thumbnail;
                        }
                    }
                    catch
                    {
                    }

                    if (bitmap == null)
                        //Manualy generate Thumbnail (alternative to _UPDATETHUMBSNOW)
                        try
                        {
                            using (var transaction = db.TransactionManager.StartTransaction())
                            {
                                var id = lm.GetLayoutId(Name);
                                var layout = transaction.GetObject(id, OpenMode.ForRead) as Layout;
                                bitmap = layout?.RenderLayoutSnapshot();
                            }
                        }
                        catch
                        {
                        }
                }

                if (bitmap == null) bitmap = new Bitmap(100, 100);
            }

            if (bitmap != null) result = bitmap.CreateBitmapSourceFromBitmap();
        }
        catch (Exception)
        {
        }

        return result;
    }
}
