using System.Globalization;
using System.Text;
using ClaimLens.Api.Domain;

namespace ClaimLens.Api.Services;

/// <summary>
/// Garde-fou : un fait n'est accepté que si sa citation existe dans la déclaration.
/// La comparaison ignore la casse, les accents, la ponctuation et les espaces multiples.
/// </summary>
public static class EvidenceVerifier
{
    public static Analysis Verify(Analysis analysis, string declaration)
    {
        var source = Normalize(declaration);
        var faits = analysis.Faits
            .Select(f =>
            {
                var citation = Normalize(f.Citation);
                return f with { Verifie = citation.Length >= 3 && source.Contains(citation, StringComparison.Ordinal) };
            })
            .ToList();
        return analysis with { Faits = faits };
    }

    public static string Normalize(string text)
    {
        var decomposed = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().Trim();
    }
}
