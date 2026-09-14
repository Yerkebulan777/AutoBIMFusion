using System.Runtime.InteropServices;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Resolves ObjectARX exports whose public C++ names are decorated in some AutoCAD releases.
/// </summary>
internal static class AccoreNative
{
    private const string AccoreModule = "accore.dll";

    private static readonly string[] EvaluateLispNames =
    [
        "acedEvaluateLisp",
        "?acedEvaluateLisp@@YAHPEB_WAEAPEAUresbuf@@@Z"
    ];

    private static readonly string[] ReleaseResultNames =
    [
        "acutRelRb",
        "?acutRelRb@@YAHPEAUresbuf@@@Z"
    ];

    private static readonly string[] ReleaseResultModules =
    [
        "acdb26.dll",
        "acdb25.dll",
        "acdb24.dll",
        "acdb23.dll",
        AccoreModule,
        "acad.exe"
    ];

    private static readonly Lazy<EvaluateLispDelegate> EvaluateLispFunction =
        new(LoadEvaluateLisp);

    private static readonly Lazy<ReleaseResultDelegate> ReleaseResultFunction =
        new(LoadReleaseResult);

    public static int EvaluateLisp(string expression, out IntPtr result) =>
        EvaluateLispFunction.Value(expression, out result);

    public static int ReleaseResult(IntPtr result) => ReleaseResultFunction.Value(result);

    internal static bool EvaluationSucceeded(int result) => result != 0;

    internal static IntPtr ResolveEvaluateLisp(Func<string, IntPtr> resolve) =>
        ResolveExport(EvaluateLispNames, resolve);

    internal static IntPtr ResolveReleaseResult(
        Func<string, IntPtr> getModule,
        Func<IntPtr, string, IntPtr> resolve)
    {
        foreach (string moduleName in ReleaseResultModules)
        {
            IntPtr module = getModule(moduleName);
            if (module == IntPtr.Zero)
            {
                continue;
            }

            IntPtr address = ResolveExport(ReleaseResultNames, name => resolve(module, name));
            if (address != IntPtr.Zero)
            {
                return address;
            }
        }

        return IntPtr.Zero;
    }

    internal static IntPtr ResolveExport(IReadOnlyList<string> names, Func<string, IntPtr> resolve)
    {
        foreach (string name in names)
        {
            IntPtr address = resolve(name);
            if (address != IntPtr.Zero)
            {
                return address;
            }
        }

        return IntPtr.Zero;
    }

    private static EvaluateLispDelegate LoadEvaluateLisp()
    {
        IntPtr module = GetModuleHandleW(AccoreModule);
        if (module == IntPtr.Zero)
        {
            throw new DllNotFoundException("Модуль accore.dll не загружен.");
        }

        IntPtr address = ResolveEvaluateLisp(name => GetProcAddress(module, name));
        if (address == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(nameof(EvaluateLispDelegate));
        }

        return Marshal.GetDelegateForFunctionPointer<EvaluateLispDelegate>(address);
    }

    private static ReleaseResultDelegate LoadReleaseResult()
    {
        IntPtr address = ResolveReleaseResult(GetModuleHandleW, GetProcAddress);
        if (address == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(nameof(ReleaseResultDelegate));
        }

        return Marshal.GetDelegateForFunctionPointer<ReleaseResultDelegate>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int EvaluateLispDelegate(string expression, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReleaseResultDelegate(IntPtr result);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);
}
