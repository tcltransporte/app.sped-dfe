using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace application.services.Services
{
    /// <summary>
    /// Cliente HTTP/SOAP genérico para comunicação com webservices da SEFAZ usando autenticação mútua TLS via certificado A1.
    /// </summary>
    public class SefazSoapService
    {
        private readonly ILogger<SefazSoapService> _logger;

        public SefazSoapService(ILogger<SefazSoapService> logger)
        {
            _logger = logger;

            // SEFAZ exige TLS 1.2 (e em alguns casos 1.1) -- garante compatibilidade.
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls13;
        }

        /// <summary>
        /// Envia um envelope SOAP para a URL informada usando o certificado fornecido para autenticação TLS mútua.
        /// </summary>
        /// <param name="url">URL completa do endpoint do webservice da SEFAZ.</param>
        /// <param name="soapAction">Valor do header HTTP <c>SOAPAction</c>.</param>
        /// <param name="certificate">Certificado digital A1 (PFX) para autenticação cliente.</param>
        /// <param name="soapEnvelope">Envelope SOAP completo a ser enviado no corpo da requisição.</param>
        /// <returns><see cref="XmlDocument"/> com a resposta SOAP recebida da SEFAZ.</returns>
        public XmlDocument SendRequest(string url, string soapAction, X509Certificate2 certificate, XmlDocument soapEnvelope)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL is required.", nameof(url));
            if (certificate is null)
                throw new ArgumentNullException(nameof(certificate));
            if (soapEnvelope is null)
                throw new ArgumentNullException(nameof(soapEnvelope));

            using var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = ValidateServerCertificate,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };

            handler.ClientCertificates.Add(certificate);

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var envelopeXml = soapEnvelope.OuterXml;

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(envelopeXml, Encoding.UTF8, "application/soap+xml")
            };

            if (!string.IsNullOrWhiteSpace(soapAction))
                request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);

            _logger.LogInformation("Sending SOAP request to {Url} (action={SoapAction})", url, soapAction);

            using var response = httpClient.Send(request);

            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SEFAZ responded with HTTP {StatusCode} ({ReasonPhrase}). Body: {Body}",
                    (int)response.StatusCode, response.ReasonPhrase, responseBody);

                throw new HttpRequestException(
                    $"SEFAZ returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {responseBody}");
            }

            var responseDoc = new XmlDocument { PreserveWhitespace = false };
            responseDoc.LoadXml(responseBody);
            return responseDoc;
        }

        /// <summary>
        /// SEFAZ usa cadeia ICP-Brasil; em alguns ambientes (especialmente Linux/containers) os certificados raiz não estão presentes.
        /// Esse callback valida apenas erros graves de cadeia, ignorando problemas de raiz desconhecida.
        /// </summary>
        private static bool ValidateServerCertificate(
            HttpRequestMessage request,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            // Aceita quando o único problema for a cadeia/raiz não confiável (comum em ICP-Brasil em containers).
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors && chain is not null)
            {
                foreach (var status in chain.ChainStatus)
                {
                    if (status.Status != X509ChainStatusFlags.NoError &&
                        status.Status != X509ChainStatusFlags.UntrustedRoot &&
                        status.Status != X509ChainStatusFlags.PartialChain)
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
