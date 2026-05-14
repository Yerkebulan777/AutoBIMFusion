using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using System.Diagnostics;
using Exception = System.Exception;

namespace AutoBIMFusion.Plugin.Commands;

/// <summary>
/// Управляет расширенными данными объектов (XData).
/// Позволяет просматривать и удалять XData у выбранных или всех объектов чертежа.
/// </summary>
public static class XDataCommands
{
    /// <summary>
    /// Показывает XData выбранной сущности в командной строке.
    /// </summary>
    public static void Read()
    {
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();

        PromptEntityResult result = ed.GetEntity("Selectionnez un object");

        if (result.Status == PromptStatus.OK)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();

            if (result.ObjectId.GetDBObject() is Entity ent)
            {
                foreach (object item in ent.ReadXData())
                {
                    Generic.WriteMessage(item.ToString()!);
                }
            }
        }
    }

    /// <summary>
    ///     Удаляет XData у объектов, выбранных пользователем.
    /// </summary>
    public static void Remove()
    {
        Editor ed = Generic.GetEditor();

        if (ed.GetImpliedSelection(out PromptSelectionResult? AllSelectedObjectImplied))
        {
            RemoveAllXDataFromCollection(AllSelectedObjectImplied.Value.GetObjectIds());
        }
        else
        {
            (_, object Value) = ed.GetSelectionRedraw("Selectionnez des entités pour lequels vous souhaitez supprimer les XDATAs");
            RemoveAllXDataFromCollection(Value.GetObjectIds());
        }
    }

    /// <summary>
    ///  Удаляет XData у всех объектов чертежа.
    /// </summary>
    public static void RemoveAll()
    {
        Database db = Generic.GetDatabase();
        RemoveAllXDataFromCollection(db.GetAllObjects().Keys.ToList());
    }

    /// <summary>
    /// Удаляет XData у коллекции объектов, игнорируя ошибки для отдельных элементов.
    /// </summary>
    private static void RemoveAllXDataFromCollection(IList<ObjectId> objectIds)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId item in objectIds)
        {
            try
            {
                item.GetDBObject().RemoveAllXdata();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        trx.Commit();
    }
}
