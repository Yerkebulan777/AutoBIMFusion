using System.Globalization;
using System.Text;

namespace AutoBIMFusion.Common.Extensions;

internal static class StringExtensions
{
    public static IEnumerable<int> AllIndexesOf(this string OriginalString, string SearchedString)
    {
        var minIndex = OriginalString.IndexOf(SearchedString);
        while (minIndex != -1)
        {
            yield return minIndex;
            minIndex = OriginalString.IndexOf(SearchedString, minIndex + SearchedString.Length);
        }
    }

    public static string Replace(this string BaseStr, IEnumerable<char> chars, char replaceChar)
    {
        var ReplaceStr = BaseStr;
        foreach (var c in chars) ReplaceStr = ReplaceStr.Replace(c, replaceChar);

        return ReplaceStr;
    }

    public static string CapitalizeFirstLetters(this string input, int x)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        x = Min(x, input.Length);
        var firstXLetters = input[..x].ToUpper();
        var restOfTheString = input[x..];

        return firstXLetters + restOfTheString;
    }

    public static string UcFirst(this string input)
    {
        return input.ToLowerInvariant().CapitalizeFirstLetters(1);
    }

    public static string TrimStart(this string input, string prefix)
    {
        return input.StartsWith(prefix) ? input[prefix.Length..] : input;
    }

    public static string RemoveDiacritics(this string str)
    {
        if (str == null) return null;

        var chars = str
            .Normalize(NormalizationForm.FormD)
            .ToCharArray()
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    public static bool IgnoreCaseEquals(this string str1, string str2)
    {
        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }

    public static string RemoveNonNumeric(this string str)
    {
        if (str == null) return null;

        StringBuilder result = new();

        foreach (var c in str)
            if (char.IsDigit(c))
                _ = result.Append(c);

        return result.ToString();
    }

    public static string[] SplitByListString(this string input, IEnumerable<string> delimiters)
    {
        return input.Split(delimiters.ToArray(), StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] SplitUserInputByDelimiters(this string input, params string[] delimiters)
    {
        //var PossibleValuesSeparators = new List<string> { ";", "," };
        var LanguageSeparator =
            CultureInfo.CurrentUICulture.NumberFormat.NumberDecimalSeparator; //french use , as decimal separaror
        var newdelimiters = delimiters.Where(car => car.Trim() != LanguageSeparator);
        return input.SplitByListString(newdelimiters).ToArray();
    }

    public static string SanitizeToAlphanumericHyphens(this string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        StringBuilder sb = new();

        foreach (var c in input) _ = char.IsLetterOrDigit(c) ? sb.Append(c) : sb.Append('-');

        return sb.ToString();
    }
}
