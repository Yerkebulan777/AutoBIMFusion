using System.Diagnostics;
using System.Drawing.Imaging;
using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.Geometry;
using AutoBIMFusion.Common.Mist.Helpers;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = System.Drawing.Color;
using DrawingImage = System.Drawing.Image;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using MenuItem = Autodesk.AutoCAD.Windows.MenuItem;

namespace AutoBIMFusion.Common.Functions;

public static class ImageToOleConverter
{
    public static void RasterToOle()
    {
        var ed = Generic.GetEditor();
        var db = Generic.GetDatabase();

        var selectionOptions = new PromptSelectionOptions
        {
            MessageForAdding = "Selectionnez une image",
            SinglePickInSpace = false,
            SingleOnly = false,
            RejectObjectsOnLockedLayers = true
        };

        var filterList = new[]
        {
            new TypedValue((int)DxfCode.Start, "IMAGE")
        };

        var ent = ed.GetSelection(selectionOptions, new SelectionFilter(filterList));

        if (ent.Status != PromptStatus.OK) return;

        foreach (var RasterObjectId in ent.Value.GetObjectIds())

            using (var trx = db.TransactionManager.StartTransaction())
            {
                if (RasterObjectId.GetDBObject() is RasterImage rasterImage)
                {
                    var rasterImageColor = rasterImage.GetSystemDrawingColor();
                    var bitmap = DrawingImage.FromFile(rasterImage.Path);
                    var ImageHasAlpha = bitmap.PixelFormat.HasFlag(PixelFormat.Alpha);
                    const string HasAlphaWarningMessage =
                        "la transparence de l'image sera supprimée et remplacée par la couleur de l'object raster";
                    var ImageIsRotated = rasterImage.Rotation > 0;
                    const string IsRotatedWarningMessage =
                        "les OLE ne supportent pas les rotations. Un fond sera appliqué de la couleur de l'object raster";
                    if (ImageHasAlpha || ImageIsRotated)
                    {
                        var JoinedMessage = "Attention : ";
                        if (ImageHasAlpha && ImageIsRotated)
                            JoinedMessage += $"\n - {HasAlphaWarningMessage}\n - {IsRotatedWarningMessage}";
                        else if (ImageHasAlpha)
                            JoinedMessage += HasAlphaWarningMessage;
                        else if (ImageIsRotated) JoinedMessage += IsRotatedWarningMessage;
                        JoinedMessage +=
                            $"\nVoullez-vous utiliser un fond de la couleur de l'object raster ? ({rasterImageColor.R},{rasterImageColor.G},{rasterImageColor.B}). Un fond blanc sera appliqué dans le cas contraire";

                        var askOptions = new PromptKeywordOptions($"\n{JoinedMessage} [Oui/Non/Annuler] <Annuler>: ");
                        askOptions.Keywords.Add("Oui");
                        askOptions.Keywords.Add("Non");
                        askOptions.Keywords.Add("Annuler");
                        askOptions.Keywords.Default = "Annuler";
                        askOptions.AllowNone = true;
                        var askContinue = ed.GetKeywords(askOptions);

                        if (askContinue.Status != PromptStatus.OK || askContinue.StringResult == "Annuler") return;

                        if (askContinue.StringResult != "Non") rasterImageColor = Color.White;
                    }

                    var BitmapSize = bitmap.GetImageFileSize();
                    Debug.WriteLine("Bitmap Size :" + BitmapSize);

                    var clipboardType = Type.GetType("System.Windows.Forms.Clipboard, System.Windows.Forms", false);
                    if (clipboardType is null)
                    {
                        Generic.WriteMessage("Clipboard System.Windows.Forms indisponible.");
                        continue;
                    }

                    var ClipBackup = clipboardType.GetMethod("GetDataObject", Type.EmptyTypes)?.Invoke(null, null);
                    using (var RotatedImage = bitmap.RotateImage(rasterImage.Rotation, rasterImageColor))
                    {
                        try
                        {
                            clipboardType.GetMethod("Clear", Type.EmptyTypes)?.Invoke(null, null);
                            clipboardType.GetMethod("SetImage", new[] { typeof(DrawingImage) })
                                ?.Invoke(null, new object[] { RotatedImage });

                            Generic.WriteMessage(
                                $"Conversion de l'image en OLE. Taille de l'image d'origine : {RotatedImage.GetImageFileSize()}");
                        }
                        catch (Exception ex)
                        {
                            Generic.WriteMessage(ex.Message);
                            continue;
                        }
                    }


                    var RasterImagePosition = rasterImage.Position.ToPoint2d();
                    //Paste into the drawing because we cannot create a Ole2Frame in NET
                    Generic.Command("_pasteclip", RasterImagePosition);

                    try
                    {
                        //Recover clipboard
                        clipboardType.GetMethod("SetDataObject", new[] { typeof(object) })
                            ?.Invoke(null, new[] { ClipBackup });
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }

                    //Get last created entity of type Ole2Frame
                    var InsertedOLEObjectId = db.EntLast(typeof(Ole2Frame));

                    if (InsertedOLEObjectId.GetDBObject(OpenMode.ForWrite) is Ole2Frame InsertedOLE)
                    {
                        //Move OLE at the right position
                        // Positionner l'OLE à l'emplacement de l'image raster
                        var rasterExtent = rasterImage.GetExtents();
                        InsertedOLE.Position3d = rasterExtent.ToRectangle3d();

                        // Définir les propriétés de base de l'OLE
                        InsertedOLE.Layer = "0";
                        InsertedOLE.ColorIndex = 0; // ByBlock
                        InsertedOLE.Transparency = new Transparency(TransparencyMethod.ByBlock);
                        InsertedOLE.Linetype = "BYBLOCK";
                        InsertedOLE.LineWeight = LineWeight.ByBlock;

                        // Créer un bloc unique contenant l'OLE
                        var oleFileName = new FileInfo(rasterImage.Path).Name;
                        var blockName = BlockReferences.GetUniqueBlockName($"OLE_{oleFileName}");

                        var blockOrigin = Points.From3DPoint(rasterExtent.MinPoint);
                        var oleClone = InsertedOLE.Clone() as DBObject;

                        BlockReferences.Create(blockName, "OLE Definition", new DBObjectCollection { oleClone },
                            blockOrigin, false, BlockScaling.Uniform);

                        // Insérer le bloc et copier les propriétés de l'image raster
                        var blkObj = BlockReferences.InsertFromName(blockName, blockOrigin, 0, null, rasterImage.Layer,
                            rasterImage.Database.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTableRecord);

                        var blkRef = blkObj.GetDBObject(OpenMode.ForWrite) as BlockReference;
                        rasterImage.CopyPropertiesTo(blkRef);
                        Generic.WriteMessage(
                            $"Taille finale de l'image OLE dans le dessin : {Files.FormatFileSizeFromByte(blkRef.ObjectId.GetObjectByteSize())}");
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
        var FitBoundingBoxSize = FitBoundingBox.Size();
        var EntExtendSize = ent.GetExtents().Size();
        var HeightRatio = FitBoundingBoxSize.Height / EntExtendSize.Height;
        var WidthRatio = FitBoundingBoxSize.Width / EntExtendSize.Width;
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
            var mi = new MenuItem("Convertir en OLE (embed)");
            mi.Click += OnExecute;
            cme.MenuItems.Add(mi);
            var rxc = RXObject.GetClass(typeof(RasterImage));
            if (rxc is null) return;
            Application.AddObjectContextMenuExtension(rxc, cme);
        }

        public static void Detach()
        {
            var rxc = RXObject.GetClass(typeof(RasterImage));
            Application.RemoveObjectContextMenuExtension(rxc, cme);
        }

        private static void OnExecute(object? o, EventArgs e)
        {
            Generic.SendStringToExecute("SIOFORGECAD.ImageToOleConverter");
        }
    }
}
