using Autodesk.AutoCAD.GraphicsInterface;
using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Commun.Mist.DrawJigs;

public class GetPointJig : DrawJig, IDisposable
{
    private Point3d _currentPoint = Point3d.Origin;


    private string[] _keywords = [];
    private string _message = string.Empty;

    public Points BasePoint = Points.Null;
    private bool disposedValue;

    public Func<Points, GetPointJig, bool>? UpdateFunction;

    public DBObjectCollection Entities { get; set; } = new();
    public DBObjectCollection StaticEntities { get; set; } = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public (Points Point, PromptResult PromptPointResult) GetPoint(string message, params string[] keywords)
    {
        _message = message;
        _keywords = keywords;

        var result = Generic.GetEditor().Drag(this);

        if (result.Status == PromptStatus.OK) return (new Points(_currentPoint), result);

        return (Points.Null, result);
    }

    protected override SamplerStatus Sampler(JigPrompts prompts)
    {
        var ppo = new JigPromptPointOptions("\n" + _message);
        if (BasePoint != Points.Null)
        {
            ppo.UseBasePoint = true;
            ppo.BasePoint = BasePoint.SCU;
        }

        ppo.UserInputControls =
            UserInputControls.GovernedByOrthoMode |
            UserInputControls.NullResponseAccepted |
            UserInputControls.GovernedByUCSDetect;


        if (_keywords != null)
        {
            foreach (var kv in _keywords) ppo.Keywords.Add(kv);

            ppo.AppendKeywordsToMessage = true;
            ppo.UserInputControls = ppo.UserInputControls |
                                    UserInputControls.AcceptOtherInputString |
                                    UserInputControls.NullResponseAccepted;
        }


        var res = prompts.AcquirePoint(ppo);
        if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;

        if (res.Value.IsEqualTo(_currentPoint)) return SamplerStatus.NoChange;

        _currentPoint = res.Value;
        return SamplerStatus.OK;
    }

    protected override bool WorldDraw(WorldDraw draw)
    {
        if (UpdateFunction != null) _ = UpdateFunction(new Points(_currentPoint), this);
        if (Entities != null)
            foreach (Entity ent in Entities)
            {
                var clone = ent.Clone() as Entity;
                if (clone != null)
                {
                    clone.TransformBy(
                        Matrix3d.Displacement((BasePoint?.SCU ?? Point3d.Origin).GetVectorTo(_currentPoint)));
                    draw.Geometry.Draw(clone);
                    clone.Dispose();
                }
            }

        if (StaticEntities != null)
            foreach (Entity ent in StaticEntities)
            {
                var clone = ent.Clone() as Entity;
                if (clone != null)
                {
                    draw.Geometry.Draw(clone);
                    clone.Dispose();
                }
            }

        return true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Entities.DeepDispose();
                StaticEntities.DeepDispose();
            }

            disposedValue = true;
        }
    }
}
