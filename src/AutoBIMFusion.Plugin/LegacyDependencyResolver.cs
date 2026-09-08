#if NETFRAMEWORK
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AutoBIMFusion.Plugin;

/// <summary>
/// AutoCAD does not read DLL.config binding redirects. Resolve older BCL references
/// against the compatible dependencies shipped beside this plugin, without changing acad.exe.config.
/// </summary>
internal static class LegacyDependencyResolver
{
    private static readonly string PluginDirectory = Path.GetDirectoryName(typeof(LegacyDependencyResolver).Assembly.Location)!;
    private static readonly HashSet<string> Dependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Memory", "System.Buffers", "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe", "System.Threading.Tasks.Extensions",
        "System.Diagnostics.DiagnosticSource", "System.Threading.Channels",
        "Microsoft.Bcl.AsyncInterfaces", "System.Text.Json", "System.Text.Encodings.Web"
    };

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register before AutoCAD JIT-compiles command methods that reference legacy dependencies.")]
    internal static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        // The CLR can omit RequestingAssembly for JIT dependency binds. When supplied,
        // restrict it to our installation; all requests must also pass the allowlist below.
        Assembly? requester = args.RequestingAssembly;
        if (requester is not null && (requester.IsDynamic ||
            !string.Equals(Path.GetDirectoryName(requester.Location), PluginDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var requested = new AssemblyName(args.Name);
        if (requested.Name is null || !Dependencies.Contains(requested.Name)) return null;

        string path = Path.Combine(PluginDirectory, requested.Name + ".dll");
        if (!File.Exists(path)) return null;

        var available = AssemblyName.GetAssemblyName(path);
        // Explicit LINQ avoids C# 14 selecting a Span overload that itself needs Unsafe.
        if (available.Version < requested.Version ||
            !string.Equals(available.CultureName, requested.CultureName, StringComparison.OrdinalIgnoreCase) ||
            !Enumerable.SequenceEqual(available.GetPublicKeyToken() ?? Array.Empty<byte>(), requested.GetPublicKeyToken() ?? Array.Empty<byte>()))
        {
            return null;
        }

        return Assembly.LoadFrom(path);
    }
}
#endif
