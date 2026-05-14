using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Internal;
using System.Drawing;
using System.Windows.Media.Imaging;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.Extensions;

public static class LayoutManagerExtensions
{
    public static BitmapSource? GetLayoutImage(this LayoutManager lm, string Name)
    {
        Database db = Generic.GetDatabase();
        Document Doc = Generic.GetDocument();
        if (string.IsNullOrEmpty(Name))
        {
            return null;
        }

        BitmapSource? result = null;
        try
        {
            Bitmap? bitmap = Utils.GetLayoutThumbnail(Doc, Name);
            if (bitmap == null)
            {
                Database database = Doc.Database;
                if (database != null)
                {
                    try
                    {
                        using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
                        using DBObject dBObject = transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
                        DBDictionary? dBDictionary = dBObject as DBDictionary;
                        if (dBDictionary == null)
                        {
                            return null;
                        }

                        ObjectId at = dBDictionary.GetAt(Name);
                        Layout? layout = transaction.GetObject(at, OpenMode.ForRead) as Layout;
                        bitmap = layout?.Thumbnail;
                    }
                    catch
                    {
                    }

                    if (bitmap == null)
                    {
                        //Manualy generate Thumbnail (alternative to _UPDATETHUMBSNOW)
                        try
                        {
                            using Transaction transaction = db.TransactionManager.StartTransaction();
                            ObjectId id = lm.GetLayoutId(Name);
                            Layout? layout = transaction.GetObject(id, OpenMode.ForRead) as Layout;
                            bitmap = layout?.RenderLayoutSnapshot();
                        }
                        catch
                        {
                        }
                    }
                }

                bitmap ??= new Bitmap(100, 100);
            }

            if (bitmap != null)
            {
                result = bitmap.CreateBitmapSourceFromBitmap();
            }
        }
        catch (Exception)
        {
        }

        return result;
    }
}
