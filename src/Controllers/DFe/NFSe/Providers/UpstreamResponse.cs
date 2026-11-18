using System.Text.RegularExpressions;

namespace application.services.Controllers.DFe.NFSe.Providers
{
    /// <summary>
    /// Valida respostas de serviços externos (prefeitura / Ambiente Nacional).
    /// Páginas HTML de erro (IIS, proxy, gateway) nunca devem ser devolvidas como se fossem XML/JSON da NFS-e.
    /// </summary>
    public static class UpstreamResponse
    {
        public static async Task<string> ReadAsync(HttpResponseMessage response, string endpoint)
        {
            var body = await response.Content.ReadAsStringAsync();
            var trimmed = body.TrimStart();

            var isHtml = trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
                      || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);

            if (!isHtml)
                return body;

            var title = Regex.Match(body, "<title>\\s*(.*?)\\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var summary = title.Success ? Regex.Replace(title.Groups[1].Value, "\\s+", " ").Trim() : "resposta HTML inesperada";

            throw new InvalidOperationException(
                $"O servidor da prefeitura/emissor ({endpoint}) respondeu com erro HTTP {(int)response.StatusCode}: {summary}. " +
                "O problema está no serviço externo, não nos dados enviados. Tente novamente mais tarde.");
        }
    }
}
