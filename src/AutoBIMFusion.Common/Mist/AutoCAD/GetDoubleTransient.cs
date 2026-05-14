using Autodesk.AutoCAD.Colors;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public class GetDoubleTransient(DBObjectCollection Entities) : TransientBase(Entities, null)
{
    public PromptDoubleResult GetDouble(string Message, params string[] KeyWords)
    {
        Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

        CreateTransGraphics();

        PromptDoubleOptions options = new("\n" + Message)
        {
            AllowNone = true, // Permet d'appuyer sur Entrée pour valider sans taper de chiffre
            UseDefaultValue = false // Désactivé pour que "Entrée" renvoie bien le status 'None' et pas 'OK'
        };

        foreach (var KeyWord in KeyWords)
        {
            if (!string.IsNullOrWhiteSpace(KeyWord))
            {
                options.Keywords.Add(KeyWord);
            }
        }

        if (options.Keywords.Count > 0)
        {
            options.AppendKeywordsToMessage = true;
        }

        PromptDoubleResult result = ed.GetDouble(options);

        ClearTransGraphics();

        return result;
    }
}

// Variante pour conserver les couleurs d'origine des objets copiés dans l'aperçu
public class GetDoubleTransientNoColorChange : GetDoubleTransient
{
    public GetDoubleTransientNoColorChange(DBObjectCollection Entities) : base(Entities)
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
