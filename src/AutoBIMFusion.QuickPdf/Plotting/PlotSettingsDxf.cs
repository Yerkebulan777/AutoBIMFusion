using AutoBIMFusion.Common.Helpers;
using System.Globalization;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;
using Exception = System.Exception;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Пишет размер листа в DXF AcDbPlotSettings. Управляемого сеттера PlotPaperSize нет.
/// </summary>
internal static class PlotSettingsDxf
{
    private const int RtNorm = 5100;

    public static void ApplyCustomPaper(ObjectId plotSettingsId, string name, double widthMm, double heightMm)
    {
        string handle = plotSettingsId.Handle.Value.ToString("X", CultureInfo.InvariantCulture);
        string width = widthMm.ToString("0.##########", CultureInfo.InvariantCulture);
        string height = heightMm.ToString("0.##########", CultureInfo.InvariantCulture);
        string escapedName = StringUtils.EscapeForQuotedContext(name);
        EvaluateLisp(
            "(progn (setq e (handent \"" + handle + "\")) (if (null e) nil (progn " +
            "(setq data (entget e) active nil result nil) (foreach pair data " +
            "(if (= (car pair) 100) (setq active (= (cdr pair) \"AcDbPlotSettings\"))) " +
            "(cond ((and active (= (car pair) 4)) (setq pair (cons 4 \"" + escapedName + "\"))) " +
            "((and active (= (car pair) 44)) (setq pair (cons 44 " + width + "))) " +
            "((and active (= (car pair) 45)) (setq pair (cons 45 " + height + "))) " +
            "((and active (= (car pair) 40)) (setq pair (cons 40 0.0))) " +
            "((and active (= (car pair) 41)) (setq pair (cons 41 0.0))) " +
            "((and active (= (car pair) 42)) (setq pair (cons 42 0.0))) " +
            "((and active (= (car pair) 43)) (setq pair (cons 43 0.0))) " +
            "((and active (= (car pair) 72)) (setq pair (cons 72 1))) " +
            "((and active (= (car pair) 73)) (setq pair (cons 73 0))) " +
            "((and active (= (car pair) 76)) (setq pair (cons 76 0))) " +
            "((and active (= (car pair) 77)) (setq pair (cons 77 2)))) " +
            "(setq result (cons pair result))) (entmod (reverse result)))))");
    }

    private static void EvaluateLisp(string expression)
    {
        IntPtr buffer = IntPtr.Zero;
        int status;
        try
        {
            status = acedEvaluateLisp(expression, out buffer);
        }
        catch (DllNotFoundException ex)
        {
            throw new QuickPdfException("Не удалось вызвать LISP-ядро AutoCAD для пользовательского формата.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new QuickPdfException("Текущий AutoCAD не экспортирует acedEvaluateLisp для пользовательского формата.", ex);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                try
                {
                    acutRelRb(buffer);
                }
                catch (Exception)
                {
                    // Best-effort resbuf release.
                }
            }
        }

        if (status != RtNorm)
        {
            throw new QuickPdfException("AutoCAD отклонил временную конфигурацию пользовательского формата.");
        }
    }

    [DllImport("accore.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int acedEvaluateLisp(string lispExpression, out IntPtr result);

    [DllImport("accore.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void acutRelRb(IntPtr resultBuffer);
}
