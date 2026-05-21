using AutoBIMFusion.Common.Configuration;
using AutoBIMFusion.Common.Helpers;
using Autodesk.AutoCAD.AcInfoCenterConn;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.Internal.InfoCenter;
using System.Diagnostics;
using System.Reflection;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Common.AcadSupport;

public static class AcadContext
{
    public static readonly Tolerance LowTolerance = new(1e-3, 1e-3);
    public static readonly Tolerance MediumTolerance = new(1e-5, 1e-5);

    public static void ReadWriteToFileResource(string name, string ToFilePath)
    {
        byte[]? ressource_bytes = Resources.ResourceManager.GetObject(name) as byte[];
        if (!FileUtil.IsFileLockedOrReadOnly(ToFilePath))
        {
            File.WriteAllBytes(ToFilePath, ressource_bytes);
        }
    }

    public static string GetCurrentDocumentPath()
    {
        Document doc = GetDocument();
        if (Path.GetDirectoryName(doc.Name).Equals(string.Empty))
        {
            return "";
        }

        HostApplicationServices hs = HostApplicationServices.Current;
        string FilePath = hs.FindFile(doc.Name, doc.Database, FindFileHint.Default);
        string directory = new FileInfo(FilePath).Directory.FullName;
        Debug.WriteLine(directory);
        return directory;
    }

    public static double FormatNumberForPrint(double Number)
    {
        short DisplayPrecision = (short)Application.GetSystemVariable("LUPREC");
        return Round(Number, DisplayPrecision);
    }

    public static string TryFormatIfNumberForPrint(object obj)
    {
        return double.TryParse(obj.ToString(), out double Number)
            ? FormatNumberForPrint(Number).ToString()
            : obj.ToString();
    }

    public static void WriteMessage(object message)
    {
        Editor ed = GetEditor();
        ed?.WriteMessage("\n" + message.ToString().Replace('\n', ' ').Replace("\r", "") + "\n");
    }

    public static void WriteInfoCenterBalloonMessage(object message)
    {
        InfoCenterManager infoCenterManager = new();
        ResultItem resultItem = new()
        {
            Category = GetExtensionDLLName(),
            Title = message.ToString()
        };
        infoCenterManager.PaletteManager.ShowBalloon(resultItem);
    }

    public static void LoadLispFromStringCommand(string lispCode)
    {
        Document doc = GetDocument();
        string loadCommand = "(eval '" + lispCode + ")";
        doc.SendStringToExecute(loadCommand, true, false, false);
    }

    public static string GetExtensionDLLName()
    {
        return Assembly.GetExecutingAssembly().GetName().Name;
    }

    public static string GetExtensionDLLLocation()
    {
        return Assembly.GetExecutingAssembly().Location;
    }

    public static ObjectId AddFontStyle(string font)
    {
        Document doc = GetDocument();
        Database db = GetDatabase();
        using Transaction newTransaction = doc.TransactionManager.StartTransaction();
        var newBlockTable = newTransaction.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
        BlockTableRecord newBlockTableRecord = GetCurrentSpaceBlockTableRecord(newTransaction);
        var newTextStyleTable = newTransaction.GetObject(db.TextStyleTableId, OpenMode.ForRead) as TextStyleTable;

        if (!newTextStyleTable.Has(font.ToUpperInvariant()))
        {
            newTextStyleTable.UpgradeOpen();
            TextStyleTableRecord newTextStyleTableRecord = new()
            {
                FileName = font,
                Name = font.ToUpperInvariant()
            };
            _ = newTextStyleTable.Add(newTextStyleTableRecord);
            newTransaction.AddNewlyCreatedDBObject(newTextStyleTableRecord, true);
        }

        newTransaction.Commit();
        return newTextStyleTable[font];
    }

    public static Transparency GetTransparencyFromAlpha(int Alpha)
    {
        byte AlphaByte = (byte)(255 * (100 - Alpha) / 100);
        return new Transparency(AlphaByte);
    }

    public static Document GetDocument()
    {
        return Application.DocumentManager.MdiActiveDocument;
    }

    public static Transaction GetTrans()
    {
        Database db = GetDatabase();
        return db.TransactionManager.StartTransaction();
    }

    public static DwgVersion GetSaveVersion()
    {
        Database db = GetDatabase();
        return db.OriginalFileSavedByVersion;
    }

    public static DocumentLock GetLock()
    {
        Document doc = GetDocument();
        return doc.GetLock();
    }

    public static DocumentLock GetLock(this Document doc)
    {
        return doc?.LockDocument();
    }

    public static Database GetDatabase()
    {
        return HostApplicationServices.WorkingDatabase;
    }

    public static BlockTableRecord GetCurrentSpaceBlockTableRecord(Transaction acTrans,
        OpenMode openMode = OpenMode.ForWrite)
    {
        Database db = GetDatabase();
        return acTrans.GetObject(db.CurrentSpaceId, openMode) as BlockTableRecord;
    }

    public static Editor GetEditor()
    {
        return GetDocument().Editor;
    }

    public static void SendStringToExecute(string Command, bool Echo = true)
    {
        Document doc = GetDocument();
        doc.SendStringToExecute(string.Concat(Command, ' '), true, false, Echo);
    }

    public static void SetSystemVariable(string Name, object Value, bool EchoChanges = true)
    {
        object? OldValue = GetSystemVariable(Name);
        if (OldValue is null)
        {
            Debug.WriteLine("La variable " + Name + " n'existe pas !");
            return;
        }

        if (OldValue?.ToString() != Value?.ToString())
        {
            if (EchoChanges)
            {
                WriteMessage("Changement de la variable " + Name + " de " + OldValue + " a " + Value + ".");
            }

            Application.SetSystemVariable(Name, Value);
        }
    }

    public static object GetSystemVariable(string Name)
    {
        return Application.TryGetSystemVariable(Name);
    }

    public static void Command(params object[] args)
    {
        short cmdecho = (short)Application.GetSystemVariable("CMDECHO");
        Application.SetSystemVariable("CMDECHO", 0);
        Editor ed = GetEditor();
        ed.Command(args);
        Application.SetSystemVariable("CMDECHO", cmdecho);
    }

    public static void Regen()
    {
        Editor ed = GetEditor();
        ed.Regen();
    }

    public static void RegenCommand()
    {
        SendStringToExecute("_.REGEN", false);
    }

    public static void RegenALLCommand()
    {
        SendStringToExecute("_.REGENALL", false);
    }

    public static void UpdateScreen()
    {
        GetEditor().UpdateScreen();
        Application.UpdateScreen();
    }

    public static async Task CommandAsync(params object[] args)
    {
        short cmdecho = (short)Application.GetSystemVariable("CMDECHO");
        Application.SetSystemVariable("CMDECHO", 0);
        Editor ed = GetEditor();
        await ed.CommandAsync(args);
        Application.SetSystemVariable("CMDECHO", cmdecho);
    }

    public static void CommandInApplicationContext(params object[] args)
    {
        try
        {
            Application.DocumentManager.ExecuteInApplicationContext(_ => Command(args), null);
        }
        catch (Exception ex)
        {
            WriteMessage("Exception: " + ex.Message);
        }
    }

    public static async Task CommandAsyncInCommandContext(params object[] args)
    {
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(async _ => await CommandAsync(args), null);
        }
        catch (Exception ex)
        {
            WriteMessage("Exception: " + ex.Message);
        }
    }
}
