using System.Globalization;
using System.Text;

public static class XmlSanitizer
{
    public static string RemoverAcentos(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;

        var normalized = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalized)
        {
            if (Char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}