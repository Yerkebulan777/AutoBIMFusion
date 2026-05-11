using System.Runtime.InteropServices;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Сравнитель строк с использованием естественной сортировки Windows (shlwapi.dll).
///     Обеспечивает порядок: file2.dwg перед file10.dwg.
/// </summary>
internal sealed class WindowsNaturalComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        return ReferenceEquals(x, y) ? 0 : x is null ? -1 : y is null ? 1 : StrCmpLogicalW(x, y);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);
}
