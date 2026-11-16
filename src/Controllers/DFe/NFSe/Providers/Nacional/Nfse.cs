using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace application.services.Controllers.DFe.NFSe.Providers.Nacional
{
    public sealed class NacionalNfseProvider : INFSeProvider
    {
        private const string DefaultEndpointProducao = "https://sefin.nfse.gov.br/SefinNacional/nfse";
        private const string DefaultEndpointHomologacao = "https://sefin.producaorestrita.nfse.gov.br/SefinNacional/nfse";

        private readonly NfseProvidersXmlStore configStore;

        public NacionalNfseProvider(NfseProvidersXmlStore configStore)
        {
            this.configStore = configStore;
        }

        public string Key => "NACIONAL";

        public string Name => "NFS-e Nacional (SEFIN/ADN)";

        public bool Supports(NFSeProviderContext context)
        {
            return configStore.GetConfig().SupportsProvider(Key, context.RequestedProviderKey, context.CodigoMunicipio, context.CandidateMunicipios);
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML DPS vazio ou inválido.");

            var doc = LoadTargetElementAsDocument(xml, "DPS", "GerarNfseEnvio");

            EnsureUtf8XmlDeclaration(doc);
            SignByReferenceIfNeeded(doc, certificate, "DPS", "infDPS");

            var endpoint = ResolveEndpoint(doc);

            var gzipB64 = CompressGzipBase64(doc.OuterXml);
            var jsonPayload = JsonSerializer.Serialize(new { dpsXmlGZipB64 = gzipB64 });

            return await PostJsonWithCertAsync(certificate, endpoint, jsonPayload);
        }

        public async Task<string> CancelAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML de evento NFS-e vazio ou inválido.");

            var doc = LoadTargetElementAsDocument(xml, "pedRegEvento", "CancelarNfseEnvio");

            var chNfse = doc.SelectSingleNode("//*[local-name()='chNFSe']")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(chNfse))
                throw new InvalidOperationException("Tag chNFSe obrigatória para cancelamento no Padrão Nacional.");

            NormalizePedRegEventoIfUnsigned(doc, chNfse);
            EnsureUtf8XmlDeclaration(doc);
            SignByReferenceIfNeeded(doc, certificate, "pedRegEvento", "infPedReg");

            var endpoint = $"{ResolveEndpoint(doc).TrimEnd('/')}/{chNfse}/eventos";
            var gzipB64 = CompressGzipBase64(doc.OuterXml);
            var jsonPayload = JsonSerializer.Serialize(new { pedidoRegistroEventoXmlGZipB64 = gzipB64 });

            return await PostJsonWithCertAsync(certificate, endpoint, jsonPayload);
        }

        public async Task<byte[]> DownloadDanfseAsync(X509Certificate2 certificate, string chNfse, int? tipoAmbiente = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chNfse)) throw new InvalidOperationException("Chave de acesso da NFS-e (chNFSe) obrigatória para download do DANFSe.");

            var normalizedKey = chNfse.Trim();
            var useHomologation = tipoAmbiente == 2;
            var baseEndpoint = configStore.GetConfig().ResolveNacionalDanfseEndpoint(useHomologation);
            var url = $"{baseEndpoint.TrimEnd('/')}/{normalizedKey}";

            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(errorBody)
                        ? $"Falha ao baixar DANFSe (HTTP {(int)response.StatusCode})."
                        : errorBody);
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        private static void NormalizePedRegEventoIfUnsigned(XmlDocument doc, string chNfse)
        {
            if (doc.SelectSingleNode("//*[local-name()='pedRegEvento']/*[local-name()='Signature']") != null)
                return;

            if (doc.SelectSingleNode("//*[local-name()='infPedReg']") is not XmlElement infPedReg)
                return;

            var ns = infPedReg.NamespaceURI;
            var nPedRegEl = infPedReg.SelectSingleNode("*[local-name()='nPedRegEvento']") as XmlElement;
            if (nPedRegEl == null)
            {
                nPedRegEl = doc.CreateElement("nPedRegEvento", ns);
                nPedRegEl.InnerText = "001";

                if (infPedReg.SelectSingleNode("*[local-name()='chNFSe']") is XmlNode chNfseNode && chNfseNode.NextSibling != null)
                {
                    infPedReg.InsertBefore(nPedRegEl, chNfseNode.NextSibling);
                }
                else
                {
                    infPedReg.AppendChild(nPedRegEl);
                }
            }

            var nPedDigits = new string((nPedRegEl.InnerText ?? "1").Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(nPedDigits))
                nPedDigits = "1";
            var nPedPadded = nPedDigits.Length > 3 ? nPedDigits[..3] : nPedDigits.PadLeft(3, '0');

            var eventNode = infPedReg.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName.StartsWith("e", StringComparison.OrdinalIgnoreCase) && e.LocalName.Length == 7);
            var eventCode = eventNode != null ? eventNode.LocalName[1..] : "101101";

            var currentId = infPedReg.GetAttribute("Id");
            if (string.IsNullOrWhiteSpace(currentId) || (currentId.StartsWith("PRE", StringComparison.OrdinalIgnoreCase) && currentId.Length == 59))
            {
                infPedReg.SetAttribute("Id", $"PRE{chNfse}{eventCode}{nPedPadded}");
            }
        }

        private static XmlDocument LoadTargetElementAsDocument(XmlDocument source, string targetLocalName, string wrapperLocalName)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            var root = source.DocumentElement!;

            if (string.Equals(root.LocalName, wrapperLocalName, StringComparison.OrdinalIgnoreCase))
            {
                var inner = source.SelectSingleNode($"//*[local-name()='{targetLocalName}']") as XmlElement
                    ?? throw new InvalidOperationException($"Tag {targetLocalName} não encontrada dentro de {wrapperLocalName}.");
                doc.AppendChild(doc.ImportNode(inner, true));
                if (string.IsNullOrWhiteSpace(doc.DocumentElement!.NamespaceURI) && !string.IsNullOrWhiteSpace(root.NamespaceURI))
                {
                    doc.DocumentElement.SetAttribute("xmlns", root.NamespaceURI);
                }
                return doc;
            }

            doc.LoadXml(source.OuterXml);
            return doc;
        }

        private string ResolveEndpoint(XmlDocument doc)
        {
            var tpAmbText = doc.SelectSingleNode("//*[local-name()='tpAmb']")?.InnerText?.Trim();
            var useHomologation = string.Equals(tpAmbText, "2", StringComparison.OrdinalIgnoreCase);

            return configStore.GetConfig().ResolveNacionalEndpoint(useHomologation);
        }

        private static async Task<string> PostJsonWithCertAsync(X509Certificate2 certificate, string endpoint, string jsonPayload)
        {
            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.PostAsync(endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }

        private static void SignByReferenceIfNeeded(XmlDocument doc, X509Certificate2 certificate, string signatureParentLocalName, string targetLocalName)
        {
            if (doc.SelectSingleNode($"//*[local-name()='{signatureParentLocalName}']/*[local-name()='Signature']") != null)
                return;

            var targetEl = doc.SelectSingleNode($"//*[local-name()='{targetLocalName}']") as XmlElement
                ?? throw new InvalidOperationException($"Tag {targetLocalName} não encontrada para assinatura.");

            var id = targetEl.GetAttribute("Id");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"A tag {targetLocalName} precisa possuir o atributo Id para assinatura.");

            var signingKey = certificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Certificado inválido: não possui chave privada RSA.");

            var signedXml = new SignedXml(doc)
            {
                SigningKey = signingKey
            };

            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            var reference = new Reference($"#{id}")
            {
                DigestMethod = SignedXml.XmlDsigSHA1Url
            };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(reference);

            signedXml.KeyInfo = new KeyInfo();
            signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.ComputeSignature();

            doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static void EnsureUtf8XmlDeclaration(XmlDocument doc)
        {
            if (doc.FirstChild is XmlDeclaration existingDeclaration)
            {
                existingDeclaration.Encoding = "UTF-8";
                return;
            }

            var declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            doc.InsertBefore(declaration, doc.DocumentElement);
        }

        private static string CompressGzipBase64(string xml)
        {
            var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var bytes = utf8WithoutBom.GetBytes(xml);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray());
        }
    }
}

