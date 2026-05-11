namespace SioForgeCAD.Commun.Drawing;

public static class Groups
{
    public static ObjectId Create(string Name, string Description, ObjectIdCollection EntitiesObjectIdCollection)
    {
        var db = Generic.GetDatabase();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var grp = new Group(Description, true);
            var gd = db.GroupDictionaryId.GetDBObject(OpenMode.ForWrite) as DBDictionary;
            var DuplicateNameIndex = 0;

            var GroupName = SymbolUtilityServices.RepairSymbolName(Name, false);

            while (gd.Contains(GroupName))
            {
                DuplicateNameIndex++;
                GroupName = $"{Name}_{DuplicateNameIndex}";
            }

            var grpId = gd.SetAt(GroupName, grp);
            tr.AddNewlyCreatedDBObject(grp, true);
            grp.InsertAt(0, EntitiesObjectIdCollection);
            tr.Commit();
            return grpId;
        }
    }
}
