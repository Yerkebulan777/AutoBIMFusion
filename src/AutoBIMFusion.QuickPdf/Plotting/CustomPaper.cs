using Autodesk.AutoCAD.DatabaseServices;
using AutoBIMFusion.QuickPdf.Media;
using System.Globalization;
using Exception = System.Exception;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Временная named-конфигурация с точным размером листа (DXF 44/45, нулевые поля).
///     Нужна, потому что PlotSettings.PlotPaperSize только для чтения.
/// </summary>
internal sealed class CustomPaper : IDisposable
{
    private readonly Database _database;
    private readonly ObjectId _id;
    private bool _disposed;

    private CustomPaper(Database database, ObjectId id)
    {
        _database = database;
        _id = id;
    }

    public static CustomPaper Bind(Database database, PlotSettings settings, double widthMm, double heightMm)
    {
        string configName = UniqueName(database);
        ObjectId id;
        using (Transaction tr = database.TransactionManager.StartTransaction())
        {
            DBDictionary dictionary = (DBDictionary)tr.GetObject(database.PlotSettingsDictionaryId, OpenMode.ForWrite);
            PlotSettings config = new(true);
            config.CopyFrom(settings);
            config.PlotSettingsName = configName;
            dictionary.SetAt(configName, config);
            tr.AddNewlyCreatedDBObject(config, true);
            id = config.ObjectId;
            tr.Commit();
        }

        CustomPaper paper = new(database, id);
        try
        {
            PlotSettingsDxf.ApplyCustomPaper(id, configName, widthMm, heightMm);

            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                PlotSettings config = (PlotSettings)tr.GetObject(id, OpenMode.ForRead);
                Point2d size = config.PlotPaperSize;
                if (Abs(size.X - widthMm) > ExactPaper.SizeToleranceMm || Abs(size.Y - heightMm) > ExactPaper.SizeToleranceMm)
                {
                    throw new QuickPdfException("AutoCAD отклонил размеры временной конфигурации.");
                }

                settings.CopyFrom(config);
                tr.Commit();
            }

            return paper;
        }
        catch
        {
            paper.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed || _id.IsNull)
        {
            return;
        }

        _disposed = true;
        try
        {
            using Transaction tr = _database.TransactionManager.StartTransaction();
            if (_id.IsErased)
            {
                tr.Commit();
                return;
            }

            PlotSettings config = (PlotSettings)tr.GetObject(_id, OpenMode.ForWrite);
            DBDictionary dictionary = (DBDictionary)tr.GetObject(_database.PlotSettingsDictionaryId, OpenMode.ForWrite);
            string name = config.PlotSettingsName;
            if (dictionary.Contains(name))
            {
                dictionary.Remove(name);
            }
            else if (!config.IsErased)
            {
                config.Erase();
            }

            tr.Commit();
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException)
        {
            // Best-effort cleanup of the temporary page setup.
        }
    }

    private static string UniqueName(Database database)
    {
        string baseName = "QuickPDF_" + Environment.TickCount.ToString(CultureInfo.InvariantCulture);
        using Transaction tr = database.TransactionManager.StartTransaction();
        DBDictionary dictionary = (DBDictionary)tr.GetObject(database.PlotSettingsDictionaryId, OpenMode.ForRead);
        string name = baseName;
        int suffix = 0;
        while (dictionary.Contains(name))
        {
            suffix++;
            name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        tr.Commit();
        return name;
    }
}
