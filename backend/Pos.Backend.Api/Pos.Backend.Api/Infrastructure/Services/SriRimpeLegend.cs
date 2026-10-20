using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class SriRimpeLegend
{
    private const string Generic = "CONTRIBUYENTE R\u00c9GIMEN RIMPE";
    private const string Popular = "CONTRIBUYENTE NEGOCIO POPULAR - R\u00c9GIMEN RIMPE";

    public static XElement? ResolveElement(string? regime)
    {
        if (string.IsNullOrWhiteSpace(regime)) return null;

        var decomposed = regime.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var unaccented = new string(decomposed.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var label = Regex.Replace(unaccented, @"\s*[-\u2013\u2014()]\s*", " ");
        label = Regex.Replace(label, @"\s+", " ").Trim();

        // Match complete labels only: free text, negations and mixed regimes are not classifications.
        var legend = label switch
        {
            "NEGOCIO POPULAR" or "RIMPE NEGOCIO POPULAR" or "NEGOCIO POPULAR RIMPE"
                or "REGIMEN RIMPE NEGOCIO POPULAR" or "NEGOCIO POPULAR REGIMEN RIMPE"
                or "CONTRIBUYENTE NEGOCIO POPULAR REGIMEN RIMPE"
                or "CONTRIBUYENTE RIMPE NEGOCIO POPULAR" => Popular,
            "RIMPE" or "REGIMEN RIMPE" or "CONTRIBUYENTE REGIMEN RIMPE"
                or "RIMPE EMPRENDEDOR" or "EMPRENDEDOR RIMPE"
                or "REGIMEN RIMPE EMPRENDEDOR" or "EMPRENDEDOR REGIMEN RIMPE"
                or "CONTRIBUYENTE REGIMEN RIMPE EMPRENDEDOR"
                or "CONTRIBUYENTE RIMPE EMPRENDEDOR" => Generic,
            _ => null
        };

        return legend is null ? null : new XElement("contribuyenteRimpe", legend);
    }
}
