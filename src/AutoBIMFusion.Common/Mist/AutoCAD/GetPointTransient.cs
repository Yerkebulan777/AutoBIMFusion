using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.GraphicsInterface;
using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Commun;

public class TransientBase(DBObjectCollection Entities, Func<Points, Dictionary<string, string>> UpdateFunction) : IDisposable
{
    private Func<Points, Dictionary<string, string>> UpdateFunction { get; } = UpdateFunction;
    private DBObjectCollection Entities { get; set; } = Entities;
    private DBObjectCollection? StaticEntities { get; set; }
    public List<Drawable> Drawable { get; } = new List<Drawable>();
    public List<Drawable> StaticDrawable { get; } = new List<Drawable>();

    public DBObjectCollection SetEntities
    {
        set
        {
            EraseTransients();
            DisposeDrawable();
            Entities = value;
            CreateTransGraphics();
        }
    }

    public DBObjectCollection SetStaticEntities
    {
        set
        {
            EraseTransients();
            DisposeStaticDrawable();
            StaticEntities = value;
            CreateTransGraphics();
        }
    }

    public DBObjectCollection GetEntities => Entities ?? new DBObjectCollection();

    public DBObjectCollection GetStaticEntities => StaticEntities ?? new DBObjectCollection();

    public void Dispose()
    {
        DisposeEntities();
        DisposeStaticEntities();

        DisposeDrawable();
        DisposeStaticDrawable();

        ClearTransGraphics();

        GC.SuppressFinalize(this);
    }

    public virtual void UpdateTransGraphics(Point3d curPt, Point3d moveToPt)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var db = doc.Database;

        Dictionary<string, string> Values;
        if (UpdateFunction != null)
            Values = UpdateFunction(new Points(moveToPt));
        else
            Values = new Dictionary<string, string>();
        for (var i = 0; i < Drawable.Count; i++)
        {
            var e = Drawable[i] as Entity;
            if (e is BlockReference blockReference)
                // Open the block reference for write
                using (var trx = db.TransactionManager.StartTransaction())
                {
                    if (!blockReference.IsWriteEnabled) blockReference.UpgradeOpen();

                    // Loop through the attributes of the block reference
                    foreach (var attId in blockReference.AttributeCollection)
                        if (attId is AttributeReference AttributeElement)
                        {
                            var AttributeDefinitionName = AttributeElement.Tag.ToUpperInvariant();
                            AttributeElement.Color = GetTransGraphicsColor(AttributeElement, false);
                            if (Values?.ContainsKey(AttributeDefinitionName) == true)
                                if (Values.TryGetValue(AttributeDefinitionName, out var AttributeDefinitionTargetValue))
                                    AttributeElement.TextString = AttributeDefinitionTargetValue;
                        }

                    trx.Commit();
                }

            TransformEntities(e, curPt, moveToPt);
            RedrawTransEntities(Drawable[i]);
        }
    }

    public virtual void TransformEntities(Entity entity, Point3d currentPoint, Point3d destinationPoint)
    {
    }

    public static void RedrawTransEntities(Drawable entity)
    {
        TransientManager.CurrentTransientManager.UpdateTransient(entity,
            TransientManager.CurrentTransientManager.GetViewPortsNumbers());
    }

    public virtual void EraseTransients()
    {
        TransientManager.CurrentTransientManager.EraseTransients(
            TransientDrawingMode.DirectShortTerm,
            128, TransientManager.CurrentTransientManager.GetViewPortsNumbers()
        );
    }

    public void DisposeDrawable()
    {
        if (Drawable != null)
        {
            foreach (var Entity in Drawable) Entity.Dispose();
            Drawable?.Clear();
        }
    }

    public void DisposeStaticDrawable()
    {
        if (StaticDrawable != null)
        {
            foreach (var Entity in StaticDrawable) Entity.Dispose();
            StaticDrawable?.Clear();
        }
    }

    public void DisposeEntities()
    {
        if (Entities != null)
        {
            foreach (DBObject item in Entities) item.Dispose();
            Entities?.Clear();
        }
    }

    public void DisposeStaticEntities()
    {
        if (StaticEntities != null)
        {
            foreach (DBObject item in StaticEntities) item.Dispose();
            StaticEntities?.Clear();
        }
    }

    public virtual void ClearTransGraphics()
    {
        // Clear the transient graphics for our drawables
        EraseTransients();

        // Dispose of them and clear the list
        DisposeDrawable();
        DisposeStaticDrawable();
    }

    public void CreateTransGraphics()
    {
        if (Entities != null)
            foreach (Entity drawable in Entities)
            {
                var DrawableClone = CreateTransGraphicsEntity(drawable, 0, false);
                Drawable.Add(DrawableClone);
            }

        if (StaticEntities != null)
            foreach (Entity drawable in StaticEntities)
            {
                var DrawableClone = CreateTransGraphicsEntity(drawable, 1, true);
                StaticDrawable.Add(DrawableClone);
            }
    }

    public virtual Color GetTransGraphicsColor(Entity Drawable, bool IsStaticDrawable)
    {
        return Color.FromColorIndex(ColorMethod.ByColor, (short)Settings.TransientPrimaryColorIndex);
    }

    public virtual Transparency GetTransGraphicsTransparency(Entity Drawable, bool IsStaticDrawable)
    {
        if (IsStaticDrawable)
        {
            const byte Alpha = 255 * (100 - 50) / 100;
            Drawable.Transparency = new Transparency(Alpha);
        }

        return Drawable.Transparency;
    }

    public Entity CreateTransGraphicsEntity(Entity EntityToMakeDrawable, int index, bool IsStaticDrawable)
    {
        var drawableClone = EntityToMakeDrawable.Clone() as Entity;
        drawableClone.Color = GetTransGraphicsColor(drawableClone, IsStaticDrawable);
        drawableClone.Transparency = GetTransGraphicsTransparency(drawableClone, IsStaticDrawable);
        TransientManager.CurrentTransientManager.AddTransient(drawableClone, TransientDrawingMode.DirectShortTerm,
            128 - index, TransientManager.CurrentTransientManager.GetViewPortsNumbers());
        return drawableClone;
    }
}

public class GetPointTransient : TransientBase
{
    public GetPointTransient(DBObjectCollection Entities, Func<Points, Dictionary<string, string>> UpdateFunction) :
        base(Entities, UpdateFunction)
    {
    }

    public (Points? Point, PromptPointResult PromptPointResult) GetPoint(object Message, Points OriginPoint,
        bool AllowNone, params string[] KeyWords)
    {
        var ed = Generic.GetEditor();
        var db = Generic.GetDatabase();

        var curPt = Point3d.Origin;
        CreateTransGraphics();

        void PointMonitorHandler(object sender, PointMonitorEventArgs e)
        {
            var pt = e.Context.ComputedPoint;
            UpdateTransGraphics(curPt, pt);
            curPt = pt;
        }

        ed.PointMonitor += PointMonitorHandler;
        var pointOptions = new PromptPointOptions("\n" + Message);
        foreach (var KeyWord in KeyWords)
        {
            if (string.IsNullOrWhiteSpace(KeyWord)) continue;
            pointOptions.Keywords.Add(KeyWord);
            pointOptions.AppendKeywordsToMessage = true;
            pointOptions.AllowArbitraryInput = true;
        }

        if (OriginPoint != Points.Null)
        {
            pointOptions.UseBasePoint = true;
            pointOptions.BasePoint = OriginPoint.SCU;
        }

        if (AllowNone) pointOptions.AllowNone = true;
        var IsNotValid = true;
        PromptPointResult InsertionPromptPointResult = null;
        while (IsNotValid)
        {
            InsertionPromptPointResult = ed.GetPoint(SetPromptPointOptions(pointOptions));
            if (InsertionPromptPointResult.Status == PromptStatus.OK)
                IsNotValid = !IsValidPoint(InsertionPromptPointResult);
            else
                break;
        }

        ed.PointMonitor -= PointMonitorHandler;
        var InsertionPointResult = Points.GetFromPromptPointResult(InsertionPromptPointResult);
        ClearTransGraphics();
        if (InsertionPromptPointResult.Status == PromptStatus.OK)
            return (InsertionPointResult, InsertionPromptPointResult);

        return (null, InsertionPromptPointResult);
    }

    public virtual PromptPointOptions SetPromptPointOptions(PromptPointOptions PromptPointOptions)
    {
        return PromptPointOptions;
    }

    public virtual bool IsValidPoint(PromptPointResult pointResult)
    {
        return true;
    }

    public override void TransformEntities(Entity entity, Point3d currentPoint, Point3d destinationPoint)
    {
        var mat = Matrix3d.Displacement(currentPoint.GetVectorTo(destinationPoint));
        entity.TransformBy(mat);
    }
}

public class GetPointTransientNoColorChange : GetPointTransient
{
    public GetPointTransientNoColorChange(DBObjectCollection Entities,
        Func<Points, Dictionary<string, string>> UpdateFunction) : base(Entities, UpdateFunction)
    {
    }

    public override Color GetTransGraphicsColor(Entity Drawable, bool IsStaticDrawable)
    {
        return Drawable.Color;
    }

    public override Transparency GetTransGraphicsTransparency(Entity Drawable, bool IsStaticDrawable)
    {
        return Drawable.Transparency;
    }
}
