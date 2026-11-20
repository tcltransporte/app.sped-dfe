using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace application.services
{
    /// <summary>
    /// Lê o corpo da requisição como XmlDocument de forma tolerante a codificação.
    /// Aceita UTF-8 (com/sem BOM), UTF-16 e ISO-8859-1/Windows-1252, normaliza tudo para texto
    /// Unicode e descarta a declaração &lt;?xml ... ?&gt;. Assim, o que segue para a SEFAZ
    /// é sempre gerado por nós em UTF-8 (sem BOM, sem declaração).
    /// </summary>
    public sealed class Utf8XmlInputFormatter : InputFormatter
    {
        private static readonly Regex DeclarationRegex = new(
            @"^\s*<\?xml[^>]*\?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EncodingRegex = new(
            @"encoding\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Utf8XmlInputFormatter()
        {
            SupportedMediaTypes.Add("application/xml");
            SupportedMediaTypes.Add("text/xml");
        }

        protected override bool CanReadType(Type type) => type == typeof(XmlDocument);

        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            using var ms = new MemoryStream();
            await context.HttpContext.Request.Body.CopyToAsync(ms);

            try
            {
                var text = Decode(ms.ToArray());
                var doc = new XmlDocument { PreserveWhitespace = false, XmlResolver = null };
                doc.LoadXml(text);
                return await InputFormatterResult.SuccessAsync(doc);
            }
            catch (XmlException ex)
            {
                context.ModelState.AddModelError(string.Empty, $"XML inválido: {ex.Message}");
                return await InputFormatterResult.FailureAsync();
            }
        }

        public static string Decode(byte[] bytes)
        {
            string text;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else
            {
                try
                {
                    // UTF-8 estrito: se houver bytes inválidos, o XML não está em UTF-8.
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));
                    var declared = EncodingRegex.Match(head);
                    Encoding fallback = Encoding.Latin1;
                    if (declared.Success)
                    {
                        try { fallback = Encoding.GetEncoding(declared.Groups[1].Value); }
                        catch (ArgumentException) { fallback = Encoding.Latin1; }
                    }
                    text = fallback.GetString(bytes);
                }
            }

            text = text.TrimStart('\uFEFF');
            return DeclarationRegex.Replace(text, string.Empty).Trim();
        }
    }
}
