using Autodesk.AutoCAD.Colors;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public class GetStringTransientNoColorChange : TransientBase
{
    public GetStringTransientNoColorChange(DBObjectCollection Entities) : base(Entities, null)
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

    public PromptResult GetString(string Message, string DefaultValue = "")
    {
        var ed = Application.DocumentManager.MdiActiveDocument.Editor;

        CreateTransGraphics();

        var options = new PromptStringOptions("\n" + Message)
        {
            AllowSpaces = false, // Empêche les espaces pour forcer la validation sur "Espace"
            DefaultValue = DefaultValue,
            UseDefaultValue = !string.IsNullOrEmpty(DefaultValue)
        };

        var result = ed.GetString(options);

        ClearTransGraphics();

        return result;
    }
}
