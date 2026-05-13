using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;

namespace AutoBIMFusion.Common.Drawing;

public static class Leaders
{
    public static void Draw(object Content, Point3d BasePointArgs, Point3d TextPositionArgs)
    {
        // Start a transaction
        var db = Generic.GetDatabase();
        var FlattenBasePoint = BasePointArgs.Flatten();
        var FlattenTextPosition = TextPositionArgs.Flatten();
        using (var acTrans = db.TransactionManager.StartTransaction())
        {
            using (var acMText = new MText())
            {
                acMText.Contents = Content.ToString();
                acMText.Width = 2;
                acMText.TextHeight = 0.05;
                if (TextPositionArgs == Point3d.Origin)
                    FlattenTextPosition = FlattenBasePoint.Add(new Point3d(0.5, 0.5, 0).GetAsVector());
                acMText.Location = FlattenTextPosition.Flatten();

                acMText.AddToDrawingCurrentTransaction();
                using (var acLdr = new Leader())
                {
                    acLdr.AppendVertex(FlattenBasePoint);
                    acLdr.AppendVertex(FlattenTextPosition);
                    acLdr.HasArrowHead = true;
                    acLdr.AddToDrawingCurrentTransaction();
                    acLdr.Annotation = acMText.ObjectId;
                    acLdr.Dimscale = 0.1;
                    acLdr.Dimgap = 0;
                    acLdr.Dimasz = 1;
                    acLdr.EvaluateLeader();
                }

                // Commit the changes and dispose of the transaction
                acTrans.Commit();
            }
        }
    }
}
