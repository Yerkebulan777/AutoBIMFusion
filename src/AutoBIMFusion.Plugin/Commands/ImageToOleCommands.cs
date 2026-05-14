using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.Geometry;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System.Diagnostics;
using System.Drawing.Imaging;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

using Color = System.Drawing.Color;
using DrawingImage = System.Drawing.Image;
using Exception = System.Exception;
using MenuItem = Autodesk.AutoCAD.Windows.MenuItem;

namespace AutoBIMFusion.Plugin.Commands;

public static class ImageToOleCommands
{
    public static void RasterToOle()
    {
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();

        PromptSelectionOptions selectionOptions = new()
        {
            MessageForAdding = "Selectionnez une image",
            SinglePickInSpace = false,
            SingleOnly = false,
            RejectObjectsOnLockedLayers = true
        };

        TypedValue[] filterList = new[]
        {
            new TypedValue((int)DxfCode.Start, "IMAGE")
        };

        PromptSelectionResult ent = ed.GetSelection(selectionOptions, new SelectionFilter(filterList));

        if (ent.Status != PromptStatus.OK)
        {
            return;
        }

        foreach (ObjectId RasterObjectId in ent.Value.GetObjectIds())
        {
            using Transaction trx = db.TransactionManager.StartTransaction();

            if (RasterObjectId.GetDBObject() is RasterImage rasterImage)
            {
                Color rasterImageColor = rasterImage.GetSystemDrawingColor();
                DrawingImage bitmap = DrawingImage.FromFile(rasterImage.Path);
                bool ImageHasAlpha = bitmap.PixelFormat.HasFlag(PixelFormat.Alpha);
                const string HasAlphaWarningMessage = "la transparence de l'image sera supprimée et remplacée par la couleur de l'object raster";
                bool ImageIsRotated = rasterImage.Rotation > 0;
                const string IsRotatedWarningMessage = "les OLE ne supportent pas les rotations. Un fond sera appliqué de la couleur de l'object raster";

                if (ImageHasAlpha || ImageIsRotated)
                {
                    string JoinedMessage = "Attention : ";
                    if (ImageHasAlpha && ImageIsRotated)
                    {
                        JoinedMessage += $"\n - {HasAlphaWarningMessage}\n - {IsRotatedWarningMessage}";
                    }
                    else if (ImageHasAlpha)
                    {
                        JoinedMessage += HasAlphaWarningMessage;
                    }
                    else if (ImageIsRotated)
                    {
                        JoinedMessage += IsRotatedWarningMessage;
                    }

                    JoinedMessage += $"\nVoullez-vous utiliser un fond de la couleur de l'object raster ? ({rasterImageColor.R},{rasterImageColor.G},{rasterImageColor.B}). Un fond blanc sera appliqué dans le cas contraire";

                    PromptKeywordOptions askOptions = new($"\n{JoinedMessage} [Oui/Non/Annuler] <Annuler>: ");
                    askOptions.Keywords.Add("Oui");
                    askOptions.Keywords.Add("Non");
                    askOptions.Keywords.Add("Annuler");
                    askOptions.Keywords.Default = "Annuler";
                    askOptions.AllowNone = true;
                    PromptResult askContinue = ed.GetKeywords(askOptions);

                    if (askContinue.Status != PromptStatus.OK || askContinue.StringResult == "Annuler")
                    {
                        return;
                    }

                    if (askContinue.StringResult != "Non")
                    {
                        rasterImageColor = Color.White;
                    }
                }

                string BitmapSize = bitmap.GetImageFileSize();
                Debug.WriteLine("Bitmap Size :" + BitmapSize);

                Type? clipboardType = Type.GetType("System.Windows.Forms.Clipboard, System.Windows.Forms", false);

                if (clipboardType is null)
                {
                    Generic.WriteMessage("Clipboard System.Windows.Forms indisponible.");
                    continue;
                }

                object? ClipBackup = clipboardType.GetMethod("GetDataObject", Type.EmptyTypes)?.Invoke(null, null);
                using (DrawingImage RotatedImage = bitmap.RotateImage(rasterImage.Rotation, rasterImageColor))
                {
                    try
                    {
                        _ = (clipboardType.GetMethod("Clear", Type.EmptyTypes)?.Invoke(null, null));
                        _ = (clipboardType.GetMethod("SetImage", new[] { typeof(DrawingImage) })
                            ?.Invoke(null, new object[] { RotatedImage }));

                        Generic.WriteMessage(
                            $"Conversion de l'image en OLE. Taille de l'image d'origine : {RotatedImage.GetImageFileSize()}");
                    }
                    catch (Exception ex)
                    {
                        Generic.WriteMessage(ex.Message);
                        continue;
                    }
                }


                Point2d RasterImagePosition = rasterImage.Position.ToPoint2d();
                //Paste into the drawing because we cannot create a Ole2Frame in NET
                Generic.Command("_pasteclip", RasterImagePosition);

                try
                {
                    //Recover clipboard
                    _ = (clipboardType.GetMethod("SetDataObject", new[] { typeof(object) })
                        ?.Invoke(null, new[] { ClipBackup }));
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine(ex);
                }

                //Get last created entity of type Ole2Frame
                ObjectId InsertedOLEObjectId = db.EntLast(typeof(Ole2Frame));

                if (InsertedOLEObjectId.GetDBObject(OpenMode.ForWrite) is Ole2Frame InsertedOLE)
                {
                    //Move OLE at the right position
                    // Positionner l'OLE à l'emplacement de l'image raster
                    Extents3d rasterExtent = rasterImage.GetExtents();
                    InsertedOLE.Position3d = rasterExtent.ToRectangle3d();

                    // Définir les propriétés de base de l'OLE
                    InsertedOLE.Layer = "0";
                    InsertedOLE.ColorIndex = 0; // ByBlock
                    InsertedOLE.Transparency = new Transparency(TransparencyMethod.ByBlock);
                    InsertedOLE.Linetype = "BYBLOCK";
                    InsertedOLE.LineWeight = LineWeight.ByBlock;

                    // Créer un bloc unique contenant l'OLE
                    string oleFileName = new FileInfo(rasterImage.Path).Name;
                    string blockName = BlockReferences.GetUniqueBlockName($"OLE_{oleFileName}");

                    Points blockOrigin = Points.From3DPoint(rasterExtent.MinPoint);
                    DBObject? oleClone = InsertedOLE.Clone() as DBObject;

                    _ = BlockReferences.Create(blockName, "OLE Definition", [oleClone!],
                        blockOrigin, false, BlockScaling.Uniform);

                    // Insérer le bloc et copier les propriétés de l'image raster
                    ObjectId blkObj = BlockReferences.InsertFromName(blockName, blockOrigin, 0, null!, rasterImage.Layer,
                        (rasterImage.Database.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTableRecord)!);

                    BlockReference? blkRef = blkObj.GetDBObject(OpenMode.ForWrite) as BlockReference;
                    rasterImage.CopyPropertiesTo(blkRef!);
                    Generic.WriteMessage(
                        $"Taille finale de l'image OLE dans le dessin : {FileUtil.FormatFileSizeFromByte(blkRef!.ObjectId.GetObjectByteSize())}");
                    // Nettoyer les objets sources
                    rasterImage.EraseObject();
                    InsertedOLE.EraseObject();
                }
                else
                {
                    Generic.WriteMessage("Une erreur s'est produite lors de la convertion.");
                    trx.Abort();
                }
            }

            trx.Commit();
        }
    }

    public static void TransformToFitBoundingBox(Ole2Frame ent, Extents3d FitBoundingBox)
    {
        ExtentsSize FitBoundingBoxSize = FitBoundingBox.Size();
        ExtentsSize EntExtendSize = ent.GetExtents().Size();
        double HeightRatio = FitBoundingBoxSize.Height / EntExtendSize.Height;
        double WidthRatio = FitBoundingBoxSize.Width / EntExtendSize.Width;
        ent.LockAspect = Abs(HeightRatio - WidthRatio) <= Generic.LowTolerance.EqualPoint;
        ent.WcsHeight = FitBoundingBoxSize.Height;
        ent.WcsWidth = FitBoundingBoxSize.Width;
    }

    public static class ContextMenu
    {
        private static ContextMenuExtension? cme;

        public static void Attach()
        {
            cme = new ContextMenuExtension();
            MenuItem mi = new("Convertir en OLE (embed)");
            mi.Click += OnExecute;
            _ = cme.MenuItems.Add(mi);
            RXClass? rxc = RXObject.GetClass(typeof(RasterImage));
            if (rxc is null)
            {
                return;
            }

            Application.AddObjectContextMenuExtension(rxc, cme);
        }

        public static void Detach()
        {
            RXClass rxc = RXObject.GetClass(typeof(RasterImage));
            Application.RemoveObjectContextMenuExtension(rxc, cme);
        }

        private static void OnExecute(object? o, EventArgs e)
        {
            Generic.SendStringToExecute("ImageToOleConverter");
        }
    }
}
