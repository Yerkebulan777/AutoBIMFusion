using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;

namespace AutoBIMFusion.Common.Drawing;

public static class Groups
{
    public static ObjectId Create(string Name, string Description, ObjectIdCollection EntitiesObjectIdCollection)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        Group grp = new(Description, true);
        DBDictionary? gd = db.GroupDictionaryId.GetDBObject(OpenMode.ForWrite) as DBDictionary;
        var DuplicateNameIndex = 0;

        var GroupName = SymbolUtilityServices.RepairSymbolName(Name, false);

        while (gd.Contains(GroupName))
        {
            DuplicateNameIndex++;
            GroupName = $"{Name}_{DuplicateNameIndex}";
        }

        ObjectId grpId = gd.SetAt(GroupName, grp);
        trx.AddNewlyCreatedDBObject(grp, true);
        grp.InsertAt(0, EntitiesObjectIdCollection);
        trx.Commit();
        return grpId;
    }
}
