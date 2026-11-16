using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace application.services.Controllers.DFe.NFSe.Providers.Nacional
{
    public sealed class NacionalNfseProvider : INFSeProvider
    {
        private const string DefaultNfseNamespace = "http://www.sped.fazenda.gov.br/nfse";
        private const string DefaultVersion = "1.01";

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

            var providerCfg = configStore.GetConfig().GetProvider(Key);
            var nfseNamespace = !string.IsNullOrWhiteSpace(providerCfg?.NfseNamespace)
                ? providerCfg.NfseNamespace
                : DefaultNfseNamespace;
            var version = !string.IsNullOrWhiteSpace(providerCfg?.Version)
                ? providerCfg.Version
                : DefaultVersion;

            var doc = PrepareNacionalDpsDocument(xml, certificate, nfseNamespace, version);
            var endpoint = ResolveEndpoint(doc);

            var gzipB64 = CompressGzipBase64(doc.OuterXml);
            var jsonPayload = JsonSerializer.Serialize(new { dpsXmlGZipB64 = gzipB64 });

            return await PostJsonWithCertAsync(certificate, endpoint, jsonPayload);
        }

        public async Task<string> CancelAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML de evento NFS-e vazio ou inválido.");

            var providerCfg = configStore.GetConfig().GetProvider(Key);
            var nfseNamespace = !string.IsNullOrWhiteSpace(providerCfg?.NfseNamespace)
                ? providerCfg.NfseNamespace
                : DefaultNfseNamespace;
            var version = !string.IsNullOrWhiteSpace(providerCfg?.Version)
                ? providerCfg.Version
                : DefaultVersion;
            var cancelEventType = !string.IsNullOrWhiteSpace(providerCfg?.CancelEventType)
                ? providerCfg.CancelEventType.Trim()
                : "e101101";

            var doc = PrepareNacionalPedRegEventoDocument(xml, certificate, nfseNamespace, version, cancelEventType);

            var chNfse = doc.SelectSingleNode("//*[local-name()='chNFSe']")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(chNfse))
                throw new InvalidOperationException("Tag chNFSe obrigatória para cancelamento no Padrão Nacional.");

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

        public async Task<object> ConsultDpsKeyAsync(X509Certificate2 certificate, string idDps, int? tipoAmbiente = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(idDps)) throw new InvalidOperationException("Identificador da DPS (numeroDps) obrigatório para consulta.");

            var useHomologation = tipoAmbiente == 2;
            var sefinEndpoint = configStore.GetConfig().ResolveNacionalEndpoint(useHomologation).TrimEnd('/');
            var sefinBase = sefinEndpoint.EndsWith("/nfse", StringComparison.OrdinalIgnoreCase)
                ? sefinEndpoint[..^5]
                : sefinEndpoint;
            var url = $"{sefinBase}/dps/{idDps.Trim()}";

            var responseJson = await GetJsonWithCertAsync(certificate, url);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            return new
            {
                idDps = TryGetJsonString(root, "idDps"),
                chNFSe = TryGetJsonString(root, "chaveAcesso"),
                tpAmb = TryGetJsonElement(root, "tipoAmbiente"),
                dhProcessamento = TryGetJsonString(root, "dataHoraProcessamento")
            };
        }

        public async Task<object> ConsultKeyAsync(X509Certificate2 certificate, string chNfse, int? tipoAmbiente = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chNfse)) throw new InvalidOperationException("Chave de acesso da NFS-e (chNFSe) obrigatória para consulta.");

            var useHomologation = tipoAmbiente == 2;
            var adnBase = configStore.GetConfig().ResolveNacionalAdnEndpoint(useHomologation).TrimEnd('/');
            var url = $"{adnBase}/NFSe/{chNfse.Trim()}/Eventos";

            var responseJson = await GetJsonWithCertAsync(certificate, url);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            string? xmlAut = null;
            if (TryGetPropertyCaseInsensitive(root, "LoteDFe", out var loteEl) &&
                loteEl.ValueKind == JsonValueKind.Array &&
                loteEl.GetArrayLength() > 0)
            {
                var firstItem = loteEl[0];
                var arquivoGzipB64 = TryGetJsonString(firstItem, "ArquivoXml");
                if (!string.IsNullOrWhiteSpace(arquivoGzipB64))
                {
                    xmlAut = DecompressGzipBase64(arquivoGzipB64);
                }
            }

            return new
            {
                chNFSe = TryGetJsonString(root, "StatusProcessamento"),
                tpAmb = TryGetJsonElement(root, "tipoAmbiente"),
                dhProcessamento = TryGetJsonString(root, "dataHoraProcessamento"),
                xmlAut
            };
        }

        private static async Task<string> GetJsonWithCertAsync(X509Certificate2 certificate, string url)
        {
            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(body)
                        ? $"Falha na consulta ao Ambiente Nacional (HTTP {(int)response.StatusCode})."
                        : body);
            }

            return body;
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string? TryGetJsonString(JsonElement element, string name)
        {
            return TryGetPropertyCaseInsensitive(element, name, out var prop)
                ? prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString()
                : null;
        }

        private static object? TryGetJsonElement(JsonElement element, string name)
        {
            if (!TryGetPropertyCaseInsensitive(element, name, out var prop))
                return null;

            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt32(out var intVal) => intVal,
                JsonValueKind.String => prop.GetString(),
                _ => prop.ToString()
            };
        }

        private static string DecompressGzipBase64(string base64)
        {
            var compressed = Convert.FromBase64String(base64.Trim());
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static XmlDocument PrepareNacionalDpsDocument(
            XmlDocument source,
            X509Certificate2 certificate,
            string nfseNamespace,
            string version)
        {
            XNamespace ns = nfseNamespace;
            var xDoc = XDocument.Parse(source.OuterXml, LoadOptions.PreserveWhitespace);

            var infDpsOriginal = xDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infDPS")
                ?? throw new InvalidOperationException("Tag infDPS não encontrada no XML de emissão.");

            var dpsOriginal = infDpsOriginal.Parent?.Name.LocalName == "DPS" ? infDpsOriginal.Parent : null;
            var hasSignature = dpsOriginal?.Elements().Any(e => e.Name.LocalName == "Signature") == true;

            var infDps = new XElement(infDpsOriginal);
            EnsureDpsId(infDps);
            ApplyNamespaceRecursive(infDps, ns);

            var dps = new XElement(ns + "DPS", new XAttribute("versao", version), infDps);
            if (hasSignature &&
                string.Equals(xDoc.Root?.Name.LocalName, "DPS", StringComparison.OrdinalIgnoreCase) &&
                dpsOriginal?.Elements().FirstOrDefault(e => e.Name.LocalName == "Signature") is XElement sigEl)
            {
                dps.Add(new XElement(sigEl));
            }

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(dps.ToString(SaveOptions.DisableFormatting));

            EnsureUtf8XmlDeclaration(doc);
            SignByReferenceIfNeeded(doc, certificate, "DPS", "infDPS");
            return doc;
        }

        private static XmlDocument PrepareNacionalPedRegEventoDocument(
            XmlDocument source,
            X509Certificate2 certificate,
            string nfseNamespace,
            string version,
            string targetEventType = "e101101")
        {
            XNamespace ns = nfseNamespace;
            var xDoc = XDocument.Parse(source.OuterXml, LoadOptions.PreserveWhitespace);

            var infPedRegOriginal = xDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infPedReg")
                ?? throw new InvalidOperationException("Tag infPedReg não encontrada no XML de cancelamento.");

            var pedRegOriginal = infPedRegOriginal.Parent?.Name.LocalName == "pedRegEvento" ? infPedRegOriginal.Parent : null;
            var hasSignature = pedRegOriginal?.Elements().Any(e => e.Name.LocalName == "Signature") == true;

            var normalizedTarget = string.Equals(targetEventType, "e101103", StringComparison.OrdinalIgnoreCase)
                ? "e101103"
                : "e101101";
            var targetXDesc = normalizedTarget == "e101103"
                ? "Solicitação de Análise Fiscal para Cancelamento de NFS-e"
                : "Cancelamento de NFS-e";

            var infPedReg = new XElement(infPedRegOriginal);
            var chNfse = infPedReg.Elements().FirstOrDefault(e => e.Name.LocalName == "chNFSe")?.Value.Trim() ?? string.Empty;

            var nPedRegEl = infPedReg.Elements().FirstOrDefault(e => e.Name.LocalName == "nPedRegEvento");
            if (nPedRegEl == null)
            {
                nPedRegEl = new XElement(ns + "nPedRegEvento", "001");
                var chNfseEl = infPedReg.Elements().FirstOrDefault(e => e.Name.LocalName == "chNFSe");
                if (chNfseEl != null)
                    chNfseEl.AddAfterSelf(nPedRegEl);
                else
                    infPedReg.Add(nPedRegEl);
            }

            var nPedDigits = DigitsOnly(nPedRegEl.Value);
            if (string.IsNullOrWhiteSpace(nPedDigits))
                nPedDigits = "1";
            var nPedPadded = nPedDigits.Length > 3 ? nPedDigits[..3] : nPedDigits.PadLeft(3, '0');
            nPedRegEl.Value = nPedPadded;

            var currentCancelEl = infPedReg.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "e101101" || e.Name.LocalName == "e101103");
            if (currentCancelEl != null)
            {
                var cMotivo = NormalizeCancelReasonCode(currentCancelEl.Elements().FirstOrDefault(e => e.Name.LocalName == "cMotivo")?.Value);
                var xMotivo = currentCancelEl.Elements().FirstOrDefault(e => e.Name.LocalName == "xMotivo")?.Value.Trim() ?? string.Empty;

                currentCancelEl.ReplaceWith(new XElement(
                    ns + normalizedTarget,
                    new XElement(ns + "xDesc", targetXDesc),
                    new XElement(ns + "cMotivo", cMotivo),
                    new XElement(ns + "xMotivo", xMotivo)));
            }

            var activeEventEl = infPedReg.Elements().FirstOrDefault(e =>
                e.Name.LocalName.StartsWith("e", StringComparison.OrdinalIgnoreCase) &&
                e.Name.LocalName.Length == 7);
            var eventCode = activeEventEl != null ? activeEventEl.Name.LocalName[1..] : normalizedTarget[1..];

            if (!string.IsNullOrWhiteSpace(chNfse))
            {
                infPedReg.SetAttributeValue("Id", $"PRE{chNfse}{eventCode}{nPedPadded}");
            }

            ApplyNamespaceRecursive(infPedReg, ns);

            var pedReg = new XElement(ns + "pedRegEvento", new XAttribute("versao", version), infPedReg);
            if (hasSignature &&
                string.Equals(xDoc.Root?.Name.LocalName, "pedRegEvento", StringComparison.OrdinalIgnoreCase) &&
                pedRegOriginal?.Elements().FirstOrDefault(e => e.Name.LocalName == "Signature") is XElement sigEl &&
                string.Equals((string?)infPedRegOriginal.Attribute("Id"), (string?)infPedReg.Attribute("Id"), StringComparison.Ordinal))
            {
                pedReg.Add(new XElement(sigEl));
            }

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(pedReg.ToString(SaveOptions.DisableFormatting));

            EnsureUtf8XmlDeclaration(doc);
            SignByReferenceIfNeeded(doc, certificate, "pedRegEvento", "infPedReg");
            return doc;
        }

        private static void EnsureDpsId(XElement infDps)
        {
            var currentId = (string?)infDps.Attribute("Id");
            if (!string.IsNullOrWhiteSpace(currentId) &&
                currentId.Trim().Length == 45 &&
                currentId.Trim().StartsWith("DPS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cLocEmi = DigitsOnly(infDps.Elements().FirstOrDefault(e => e.Name.LocalName == "cLocEmi")?.Value);
            var prest = infDps.Elements().FirstOrDefault(e => e.Name.LocalName == "prest");
            var cnpj = DigitsOnly(prest?.Elements().FirstOrDefault(e => e.Name.LocalName == "CNPJ")?.Value);
            var cpf = DigitsOnly(prest?.Elements().FirstOrDefault(e => e.Name.LocalName == "CPF")?.Value);
            var serie = DigitsOnly(infDps.Elements().FirstOrDefault(e => e.Name.LocalName == "serie")?.Value);
            var nDps = DigitsOnly(infDps.Elements().FirstOrDefault(e => e.Name.LocalName == "nDPS")?.Value);

            var tpInsc = !string.IsNullOrWhiteSpace(cnpj) ? "2" : (!string.IsNullOrWhiteSpace(cpf) ? "1" : string.Empty);
            var inscFed = !string.IsNullOrWhiteSpace(cnpj) ? cnpj : cpf;

            if (cLocEmi.Length == 7 && !string.IsNullOrWhiteSpace(tpInsc) && !string.IsNullOrWhiteSpace(inscFed) && !string.IsNullOrWhiteSpace(nDps))
            {
                var seriePadded = (string.IsNullOrWhiteSpace(serie) ? "1" : serie).PadLeft(5, '0');
                if (seriePadded.Length > 5) seriePadded = seriePadded[^5..];

                var nDpsPadded = nDps.PadLeft(15, '0');
                if (nDpsPadded.Length > 15) nDpsPadded = nDpsPadded[^15..];

                var inscPadded = inscFed.PadLeft(14, '0');
                if (inscPadded.Length > 14) inscPadded = inscPadded[^14..];

                infDps.SetAttributeValue("Id", $"DPS{cLocEmi}{tpInsc}{inscPadded}{seriePadded}{nDpsPadded}");
            }
        }

        private static string NormalizeCancelReasonCode(string? code)
        {
            var digits = DigitsOnly(code);
            return digits is "1" or "2" or "9" ? digits : "9";
        }

        private static string DigitsOnly(string? value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static void ApplyNamespaceRecursive(XElement element, XNamespace ns)
        {
            element.Name = ns + element.Name.LocalName;
            element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
            foreach (var child in element.Elements())
                ApplyNamespaceRecursive(child, ns);
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

