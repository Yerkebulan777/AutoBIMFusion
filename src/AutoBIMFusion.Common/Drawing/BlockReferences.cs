using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.AutoCAD;
using AutoBIMFusion.Common.Mist.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using System.Diagnostics;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Drawing;

public static class BlockReferences
{
    public static void ReplaceAllBlockReference(string OldBlockName, string NewBlockName, bool KeepScale = true,
        bool KeepRotation = true, bool PreserveProperties = true)
    {
        Database db = Generic.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        ObjectIdCollection objectIdCollection = new();

        // On parcourt tous les BlockTableRecord (ModelSpace, PaperSpace, blocs, etc.)
        foreach (ObjectId btrId in bt)
        {
            BlockTableRecord? btr = trx.GetObject(btrId, OpenMode.ForWrite) as BlockTableRecord;

            // Ne pas traiter les blocs anonymes (ex: ceux créés par les hachures ou dynamiques internes)
            if (btr.IsAnonymous || (!btr.IsLayout && !btr.IsFromExternalReference))
            {
                continue;
            }

            foreach (ObjectId entId in btr)
            {
                Entity? ent = entId.GetObject(OpenMode.ForRead) as Entity;
                if (ent is BlockReference br && br.GetBlockReferenceName() == OldBlockName)
                {
                    _ = objectIdCollection.Add(ent.ObjectId);
                }
            }
        }

        ReplaceAllBlockReference(objectIdCollection, NewBlockName, KeepScale, KeepRotation, PreserveProperties);
        Purge(OldBlockName);
        trx.Commit();
    }

    public static void ReplaceAllBlockReference(ObjectIdCollection objectIdCollection, string NewBlockName,
        bool KeepScale = true, bool KeepRotation = true, bool PreserveProperties = true)
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();

        using Transaction trx = db.TransactionManager.StartTransaction();
        foreach (ObjectId entid in objectIdCollection)
        {
            Entity? ent = entid.GetObject(OpenMode.ForRead) as Entity;

            if (ent is BlockReference br)
            {
                var IsEntOnLockedLayer = ent.IsEntityOnLockedLayer();
                Dictionary<string, string>? propertiesToCopy = PreserveProperties ? GetPropertiesFromBlock(trx, br) : null;
                if (IsEntOnLockedLayer)
                {
                    Layers.SetLock(ent.Layer, false);
                }

                ent.UpgradeOpen();
                BlockTableRecord? ownerBtr = trx.GetObject(br.OwnerId, OpenMode.ForWrite) as BlockTableRecord;

                ObjectId newBrObjId = InsertFromName(NewBlockName, br.Position.ToPoints(),
                    ed.GetUSCRotation(AngleUnit.Radians), propertiesToCopy, NewBlockName, ownerBtr);
                BlockReference? newBr = trx.GetObject(newBrObjId, OpenMode.ForWrite) as BlockReference;

                if (KeepScale)
                {
                    newBr.ScaleFactors = br.ScaleFactors;
                }

                if (KeepRotation)
                {
                    newBr.Rotation = br.Rotation;
                }

                newBr.Color = br.Color;
                newBr.Layer = br.Layer;

                if (!br.IsErased)
                {
                    br.Erase(true);
                }

                if (IsEntOnLockedLayer)
                {
                    Layers.SetLock(ent.Layer, true);
                }
            }
        }

        trx.Commit();
    }

    public static ObjectId Create(string Name, string Description, DBObjectCollection EntitiesDbObjectCollection,
        Points Origin, bool IsExplodable = true, BlockScaling BlockScaling = BlockScaling.Any)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTable;
        var BlockName = Name;

        if (BlockName != "*U") //creating an anonymous block
        {
            BlockName = SymbolUtilityServices.RepairSymbolName(Name, false);
        }

        if (bt.Has(BlockName))
        {
            Generic.WriteMessage($"Le bloc {Name} existe déja dans le dessin");
        }

        BlockTableRecord btr = new()
        {
            Name = BlockName,
            Comments = Description,
            Explodable = IsExplodable,
            Units = db.Insunits,
            BlockScaling = BlockScaling
        };
        if (Origin != Points.Null)
        {
            btr.Origin = Origin.SCG;
        }
        // Add the new block to the block table
        ObjectId btrId = bt.Add(btr);
        trx.AddNewlyCreatedDBObject(btr, true);

        foreach (Entity ent in EntitiesDbObjectCollection)
        {
            _ = btr.AppendEntity(ent);
            trx.AddNewlyCreatedDBObject(ent, true);
        }

        trx.Commit();
        return btrId;
    }

    public static ObjectId CreateFromExistingEnts(string Name, string Description, ObjectIdCollection SelectedIds,
        Points Origin, bool IsExplodable = true, BlockScaling BlockScaling = BlockScaling.Any, bool EraseOld = false)
    {
        //This method offer the avantage to keep associative hatch
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTable;
        var BlockName = Name;
        if (string.IsNullOrWhiteSpace(BlockName))
        {
            BlockName = "*U";
        }

            ;
        if (BlockName != "*U") //if we dont create an anonymous block
        {
            BlockName = SymbolUtilityServices.RepairSymbolName(Name, false);
        }

        if (bt.Has(BlockName))
        {
            Generic.WriteMessage($"Le bloc {Name} existe déja dans le dessin");
        }

        Database MemoryDatabase = new(true, false);
        IdMapping acIdMap = new();
        using (Transaction MemoryTransaction = MemoryDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc =
                MemoryTransaction.GetObject(MemoryDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord? acBlkTblRecNewDoc =
                MemoryTransaction.GetObject(acBlkTblNewDoc[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                    BlockTableRecord;
            try
            {
                MemoryDatabase.WblockCloneObjects(SelectedIds, acBlkTblRecNewDoc.ObjectId, acIdMap,
                    DuplicateRecordCloning.Replace, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return ObjectId.Null;
            }

            var UcsRotation = Vector3d.XAxis.GetAngleTo(ed.CurrentUserCoordinateSystem.CoordinateSystem3d.Xaxis,
                Vector3d.ZAxis);
            Matrix3d CounterRotation = Matrix3d.Rotation(-UcsRotation, Vector3d.ZAxis, new Point3d(0, 0, Origin.SCG.Z));
            Matrix3d DisplacementVector =
                Matrix3d.Displacement(Origin.SCG.Flatten().GetVectorTo(new Point3d(0, 0, Origin.SCG.Z)));

            foreach (ObjectId objId in acBlkTblRecNewDoc)
            {
                if (objId.IsValid)
                {
                    DBObject obj = objId.GetObject(OpenMode.ForWrite);
                    if (obj is Entity ent)
                    {
                        ent.TransformBy(CounterRotation * DisplacementVector);
                    }
                }
            }

            MemoryTransaction.Commit();
        }


        ObjectId Id = db.Insert(BlockName, MemoryDatabase, false);

        BlockTableRecord? blockDef = Id.GetObject(OpenMode.ForWrite) as BlockTableRecord;
        blockDef.Comments = Description;
        blockDef.Explodable = IsExplodable;
        blockDef.BlockScaling = BlockScaling;
        blockDef.Units = db.Insunits;

        if (EraseOld)
        {
            foreach (ObjectId oldent in SelectedIds)
            {
                oldent.EraseObject();
            }
        }

        trx.Commit();
        return Id;
    }


    public static ObjectId RenameBlockAndInsert(ObjectId BlockReferenceObjectId, string NewName)
    {
        if (!BlockReferenceObjectId.IsValid)
        {
            return ObjectId.Null;
        }

        ObjectIdCollection acObjIdColl = new() { BlockReferenceObjectId };

        Document ActualDocument = Generic.GetDocument();
        Database ActualDatabase = ActualDocument.Database;

        Database MemoryDatabase = new(true, false);
        IdMapping acIdMap = new();
        using (Transaction MemoryTransaction = MemoryDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc =
                MemoryTransaction.GetObject(MemoryDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord? acBlkTblRecNewDoc =
                MemoryTransaction.GetObject(acBlkTblNewDoc[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                    BlockTableRecord;
            try
            {
                MemoryDatabase.WblockCloneObjects(acObjIdColl, acBlkTblRecNewDoc.ObjectId, acIdMap,
                    DuplicateRecordCloning.Replace, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return ObjectId.Null;
            }

            ObjectId NewBlkRefObjectIdInMemoryDB = acIdMap[BlockReferenceObjectId].Value;
            BlockReference? NewBlkRef =
                MemoryTransaction.GetObject(NewBlkRefObjectIdInMemoryDB, OpenMode.ForRead) as BlockReference;

            if (NewBlkRef.IsDynamicBlock)
            {
                BlockTableRecord dynbtr =
                    (BlockTableRecord)MemoryTransaction.GetObject(NewBlkRef.DynamicBlockTableRecord, OpenMode.ForWrite);
                dynbtr.Name = NewName;
            }
            else
            {
                BlockTableRecord btr = (BlockTableRecord)MemoryTransaction.GetObject(NewBlkRef.BlockTableRecord, OpenMode.ForWrite);
                btr.Name = NewName;
            }

            MemoryTransaction.Commit();
        }

        ObjectId newBlocRefenceId = acIdMap[BlockReferenceObjectId].Value;
        if (!newBlocRefenceId.IsValid)
        {
            return ObjectId.Null;
        }

        ObjectIdCollection acObjIdColl2 = new() { newBlocRefenceId };
        IdMapping acIdMap2 = new();

        using (Generic.GetLock())
        using (Transaction ActualTransaction = ActualDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc2 =
                ActualTransaction.GetObject(ActualDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord acBlkTblRecNewDoc2 = Generic.GetCurrentSpaceBlockTableRecord(ActualTransaction);

            ActualDatabase.WblockCloneObjects(acObjIdColl2, acBlkTblRecNewDoc2.ObjectId, acIdMap2,
                DuplicateRecordCloning.Replace, false);
            ActualTransaction.Commit();
        }

        return acIdMap2[newBlocRefenceId].Value;
    }

    public static string GetUniqueBlockName(string oldName)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        var newName = oldName;


        for (var index = 1; bt.Has(newName); index++)
        {
            newName = SymbolUtilityServices.RepairSymbolName($"{oldName}_Copy{(index > 1 ? $" ({index})" : "")}",
                false);
        }

        return newName;
    }

    public static bool IsBlockExist(string BlocName)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;
        return bt.Has(BlocName);
    }


    public static ObjectIdCollection GetDynamicBlockReferences(string BlockName)
    {
        if (string.IsNullOrEmpty(BlockName))
        {
            return [];
        }

        Database db = Generic.GetDatabase();
        ObjectIdCollection result = new();

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockName))
            {
                return result;
            }

            BlockTableRecord btr = (BlockTableRecord)trx.GetObject(bt[BlockName], OpenMode.ForRead);

            if (!btr.IsDynamicBlock)
            {
                return result;
            }

            foreach (ObjectId anonBtrId in btr.GetAnonymousBlockIds())
            {
                BlockTableRecord anonBtr = (BlockTableRecord)trx.GetObject(anonBtrId, OpenMode.ForRead);
                result.Join(anonBtr.GetBlockReferenceIds(true, true));
            }

            trx.Commit();
        }

        return result;
    }

    public static BlockReference GetBlockReference(string BlocName, Point3d PositionSCG)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;
        if (!bt.Has(BlocName))
        {
            throw new Exception($"Le bloc {BlocName} n'existe pas dans le dessin");
        }

        BlockTableRecord? blockDef = bt[BlocName].GetObject(OpenMode.ForRead) as BlockTableRecord;
        trx.Commit();
        return new BlockReference(PositionSCG, blockDef.ObjectId);
    }

    public static ObjectId InsertFromName(string BlocName, Points BlocLocation, double Angle = 0,
        Dictionary<string, string> AttributesValues = null, string Layer = null, BlockTableRecord targetSpace = null)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        if (targetSpace == null)
        {
            targetSpace = Generic.GetCurrentSpaceBlockTableRecord(trx);
        }

        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;
        BlockTableRecord? blockDef = bt[BlocName].GetObject(OpenMode.ForRead) as BlockTableRecord;
        using BlockReference blockRef = GetBlockReference(BlocName, BlocLocation.SCG);
        blockRef.Color = db.Cecolor;
        blockRef.Rotation = Angle;

        if (!string.IsNullOrEmpty(Layer) && Layers.CheckIfLayerExist(Layer))
        {
            blockRef.Layer = Layer;
        }

        _ = targetSpace.AppendEntity(blockRef);
        trx.AddNewlyCreatedDBObject(blockRef, true);

        ApplyPropertiesToBlock(trx, blockDef, blockRef, AttributesValues);

        trx.Commit();
        return blockRef.ObjectId;
    }

    private static Dictionary<string, string> GetPropertiesFromBlock(Transaction trx, BlockReference br)
    {
        Dictionary<string, string> propertiesToCopy = new();

        foreach (ObjectId attId in br.AttributeCollection)
        {
            AttributeReference? attRef = trx.GetObject(attId, OpenMode.ForRead) as AttributeReference;
            if (attRef != null)
            {
                propertiesToCopy[attRef.Tag.ToUpperInvariant()] = attRef.TextString;
            }
        }

        if (br.IsDynamicBlock)
        {
            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                propertiesToCopy[prop.PropertyName.ToUpperInvariant()] = prop.Value.ToString();
            }
        }

        return propertiesToCopy;
    }

    private static void ApplyPropertiesToBlock(Transaction trx, BlockTableRecord btr, BlockReference blockRef,
        Dictionary<string, string> attributesValues)
    {
        if (attributesValues == null || attributesValues.Count == 0)
        {
            return;
        }

        //Settings legacy block attributes
        foreach (ObjectId id in btr)
        {
            DBObject obj = id.GetObject(OpenMode.ForRead);
            AttributeDefinition? attDef = obj as AttributeDefinition;
            if (attDef?.Constant == false)
            {
                var PropertyName = attDef.Tag.ToUpperInvariant();
                if (attributesValues.ContainsKey(PropertyName))
                {
                    if (attributesValues.TryGetValue(PropertyName, out var AttributeDefinitionTargetValue))
                    {
                        using AttributeReference attRef = new();
                        attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                        attRef.TextString = AttributeDefinitionTargetValue;
                        _ = blockRef.AttributeCollection.AppendAttribute(attRef);
                        trx.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }
        }

        //Settings Dynamic block attributes
        DynamicBlockReferencePropertyCollection BlocPropertyCollection = blockRef.DynamicBlockReferencePropertyCollection;
        Dictionary<string, DynamicBlockReferenceProperty> BlocPropertyCollectionDictionnary = new();
        foreach (DynamicBlockReferenceProperty BlocProperty in BlocPropertyCollection.OfType<DynamicBlockReferenceProperty>())
        {
            var PropertyName = BlocProperty.PropertyName.ToUpperInvariant();
            if (BlocPropertyCollectionDictionnary.ContainsKey(PropertyName))
            {
                continue;
            }

            BlocPropertyCollectionDictionnary.Add(PropertyName, BlocProperty);
        }

        foreach (var ValueKey in attributesValues.Keys)
        {
            if (BlocPropertyCollectionDictionnary.TryGetValue(ValueKey, out DynamicBlockReferenceProperty? BlocProperty))
            {
                var Value = ConvertValueToProperty((DwgDataType)BlocProperty.PropertyTypeCode,
                    attributesValues[ValueKey]);
                if (Value is int ValueAsInt)
                {
                    BlocProperty.Value = (short)ValueAsInt;
                }

                if (Value is double ValueAsDbl)
                {
                    BlocProperty.Value = ValueAsDbl;
                }

                if (Value is string ValueAsStr)
                {
                    BlocProperty.Value = ValueAsStr;
                }
            }
        }
    }


    public static ObjectId InsertFromNameImportIfNotExist(string BlocName, Points BlocLocation, double Angle = 0,
        Dictionary<string, string> AttributesValues = null, string Layer = null)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        ImportBlocFromBlocNameIfMissing(BlocName);
        ObjectId blockRefObjectId = InsertFromName(BlocName, BlocLocation, Angle, AttributesValues, Layer);
        trx.Commit();
        return blockRefObjectId;
    }

    public static void Purge(string BlocName)
    {
        //From https://adndevblog.typepad.com/autocad/2013/01/purging-anonymous-blocks-using-vba.html
        Database db = Generic.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        BlockTable? bt = trans.GetObject(db.BlockTableId, OpenMode.ForWrite) as BlockTable;

        foreach (ObjectId oid in bt)
        {
            BlockTableRecord? btr = trans.GetObject(oid, OpenMode.ForWrite) as BlockTableRecord;
            if (btr.Name.Equals(BlocName, StringComparison.OrdinalIgnoreCase))
            {
                if (btr.GetBlockReferenceIds(false, false).Count == 0 && !btr.IsLayout)
                {
                    btr.Erase();
                }
            }
        }

        trans.Commit();
    }

    public static DBObjectCollection InitForTransient(string BlocName, Dictionary<string, string> InitAttributesValues,
        string Layer = null)
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();
        DBObjectCollection ents = new();
        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            //The first block is added for initialising the process and then deleted. Be sure to add a value.
            ObjectId blockRef = InsertFromNameImportIfNotExist(BlocName, Points.Empty, ed.GetUSCRotation(AngleUnit.Radians),
                InitAttributesValues);
            DBObject dBObject = blockRef.GetDBObject();
            if (Layer != null && Layers.CheckIfLayerExist(Layer))
            {
                (dBObject as Entity).Layer = Layer;
            }

            blockRef.EraseObject();
            _ = ents.Add(dBObject);
            trx.Commit();
        }

        return ents;
    }

    public static void ImportBlocFromBlocNameIfMissing(string BlocName)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (!bt.Has(BlocName))
        {
            var TempFolderPath = Path.GetTempPath();
            var TempFolderFilePath = Path.Combine(TempFolderPath, $"{BlocName}.dwg");
            Generic.ReadWriteToFileResource(BlocName, TempFolderFilePath);

            Database sourceDb = new(false, true); //Temporary database to hold data for block we want to import
            try
            {
                sourceDb.ReadDwgFile(TempFolderFilePath, FileShare.Read, true,
                    ""); //Read the DWG into a side database
                _ = db.Insert(TempFolderFilePath, sourceDb, false);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Generic.WriteMessage("\nErreur : " + ex.Message);
            }
            finally
            {
                sourceDb.Dispose();
            }
        }

        trx.Commit();
    }

    private static object ConvertValueToProperty(DwgDataType dataType, string valueToConvert)
    {
        switch (dataType)
        {
            case DwgDataType.Real:
                if (double.TryParse(valueToConvert, out var convertedValueDouble))
                {
                    return convertedValueDouble;
                }

                break;
            case DwgDataType.Int16:
            case DwgDataType.Int32:
                if (int.TryParse(valueToConvert, out var convertedValueInt))
                {
                    return convertedValueInt;
                }

                break;
            case DwgDataType.Text:
                return valueToConvert;
        }

        return null;
    }

    private enum DwgDataType : short
    {
        Null = 0,
        Real = 1,
        Int32 = 2,
        Int16 = 3,
        Int8 = 4,
        Text = 5,
        BChunk = 6,
        Handle = 7,
        HardOwnershipId = 8,
        SoftOwnershipId = 9,
        HardPointerId = 10,
        SoftPointerId = 11,
        Dwg3Real = 12,
        Int64 = 13,
        NotRecognized = 19
    }
}
