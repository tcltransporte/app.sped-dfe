using application.services.Controllers.DFe.NFSe.Providers;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace application.services.Controllers.DFe.NFSe.Providers.ISSNET
{
    public static class NfseConstants
    {
        public const string NfseNamespace = "http://www.sped.fazenda.gov.br/nfse";
        public const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        public const string SoapNamespace = "http://www.sped.fazenda.gov.br/nfse";
        public const string SoapActionGerar = "http://www.sped.fazenda.gov.br/nfse/GerarNfse";
        public const string Version = "1.01";
    }

    public sealed class IssNetGoianiaProvider : INFSeProvider
    {
        private readonly NfseProvidersXmlStore configStore;

        public IssNetGoianiaProvider(NfseProvidersXmlStore configStore)
        {
            this.configStore = configStore;
        }

        public string Key => "ISSNET";

        public string Name => "ISS.NET (Padrão Nacional v1.01)";

        public bool Supports(NFSeProviderContext context)
        {
            return configStore.GetConfig().SupportsProvider(Key, context.RequestedProviderKey, context.CodigoMunicipio, context.CandidateMunicipios);
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML de NFS-e vazio ou inválido.");

            var config = configStore.GetConfig();
            var providerCfg = config.GetProvider(Key);

            var nfseNamespace = !string.IsNullOrWhiteSpace(providerCfg?.NfseNamespace)
                ? providerCfg.NfseNamespace
                : NfseConstants.NfseNamespace;
            var soapNamespace = !string.IsNullOrWhiteSpace(providerCfg?.SoapNamespace)
                ? providerCfg.SoapNamespace
                : NfseConstants.SoapNamespace;
            var soapAction = !string.IsNullOrWhiteSpace(providerCfg?.SoapActionGerar)
                ? providerCfg.SoapActionGerar
                : NfseConstants.SoapActionGerar;
            var version = !string.IsNullOrWhiteSpace(providerCfg?.Version)
                ? providerCfg.Version
                : NfseConstants.Version;

            var codigoMunicipio =
                ExtractTagValue(xml, "locPrest/cLocPrestacao") ??
                ExtractTagValue(xml, "cLocEmi") ??
                ExtractTagValue(xml, "Servico/CodigoMunicipio") ??
                ExtractTagValue(xml, "MunicipioIncidencia") ??
                ExtractTagValue(xml, "Prestador/Endereco/CodigoMunicipio");

            var tpAmb = ExtractTagValue(xml, "tpAmb");
            var useHomologation = string.Equals(tpAmb, "2", StringComparison.OrdinalIgnoreCase);

            var endpoint = config.ResolveIssNetEndpoint(codigoMunicipio, useHomologation);

            var isDpsPayload = xml.SelectSingleNode("//*[local-name()='infDPS']") != null;
            var gerarNfseEnvio = isDpsPayload
                ? PrepareDpsGerarNfseEnvio(xml, certificate, nfseNamespace)
                : PrepareGerarNfseEnvio(xml, certificate, nfseNamespace);

            var soapEnvelope = CreateSoapEnvelope(gerarNfseEnvio.OuterXml, nfseNamespace, soapNamespace, version, "GerarNfse");

            return await SendSoapRequestAsync(certificate, endpoint, soapEnvelope, soapAction);
        }

        public async Task<string> CancelAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML de cancelamento NFS-e vazio ou inválido.");

            var config = configStore.GetConfig();
            var providerCfg = config.GetProvider(Key);

            var nfseNamespace = !string.IsNullOrWhiteSpace(providerCfg?.NfseNamespace)
                ? providerCfg.NfseNamespace
                : NfseConstants.NfseNamespace;
            var soapNamespace = !string.IsNullOrWhiteSpace(providerCfg?.SoapNamespace)
                ? providerCfg.SoapNamespace
                : NfseConstants.SoapNamespace;
            var version = !string.IsNullOrWhiteSpace(providerCfg?.Version)
                ? providerCfg.Version
                : NfseConstants.Version;

            var chNfse = ExtractTagValue(xml, "chNFSe");
            var munFromKey = !string.IsNullOrWhiteSpace(chNfse) && chNfse.Length >= 7 && chNfse.Take(7).All(char.IsDigit)
                ? chNfse[..7]
                : null;

            var codigoMunicipio =
                ExtractTagValue(xml, "IdentificacaoNfse/CodigoMunicipio") ??
                ExtractTagValue(xml, "CodigoMunicipio") ??
                munFromKey;

            var tpAmb = ExtractTagValue(xml, "tpAmb");
            var useHomologation = string.Equals(tpAmb, "2", StringComparison.OrdinalIgnoreCase);
            var endpoint = config.ResolveIssNetEndpoint(codigoMunicipio, useHomologation);

            var soapMethod = !string.IsNullOrWhiteSpace(providerCfg?.SoapCancelRequestElement)
                ? providerCfg.SoapCancelRequestElement
                : "CancelarNfse";
            var soapAction = !string.IsNullOrWhiteSpace(providerCfg?.SoapActionCancelar)
                ? providerCfg.SoapActionCancelar
                : "http://www.sped.fazenda.gov.br/nfse/CancelarNfse";

            var cancelEventType = !string.IsNullOrWhiteSpace(providerCfg?.CancelEventType)
                ? providerCfg.CancelEventType.Trim()
                : "e101103";

            string cancelPayloadXml;
            if (xml.SelectSingleNode("//*[local-name()='infPedReg']") != null)
            {
                var cancelarEnvio = PreparePedRegCancelarNfseEnvio(xml, certificate, nfseNamespace, version, cancelEventType);
                cancelPayloadXml = cancelarEnvio.OuterXml;
            }
            else
            {
                var doc = Clone(xml);
                EnsureNamespace(doc.DocumentElement!, nfseNamespace);
                SignByReferenceIdIfNeeded(doc, certificate, "Pedido", "InfPedidoCancelamento", exclusiveC14n: true);
                cancelPayloadXml = doc.DocumentElement!.OuterXml;
            }

            var soapEnvelope = CreateSoapEnvelope(cancelPayloadXml, nfseNamespace, soapNamespace, version, soapMethod);
            return await SendSoapRequestAsync(certificate, endpoint, soapEnvelope, soapAction);
        }

        private static XmlElement PreparePedRegCancelarNfseEnvio(
            XmlDocument source,
            X509Certificate2 certificate,
            string nfseNamespace,
            string version,
            string targetEventType = "e101103")
        {
            XNamespace ns = nfseNamespace;
            var xDoc = XDocument.Parse(source.OuterXml, LoadOptions.PreserveWhitespace);

            var pedRegOriginal = xDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "pedRegEvento")
                ?? throw new InvalidOperationException("Tag pedRegEvento não encontrada no XML de cancelamento.");

            var infPedRegOriginal = pedRegOriginal.Elements().FirstOrDefault(e => e.Name.LocalName == "infPedReg")
                ?? throw new InvalidOperationException("Tag infPedReg não encontrada no XML de cancelamento.");

            var normalizedTarget = string.Equals(targetEventType, "e101101", StringComparison.OrdinalIgnoreCase)
                ? "e101101"
                : "e101103";
            var targetXDesc = normalizedTarget == "e101101"
                ? "Cancelamento de NFS-e"
                : "Solicitação de Análise Fiscal para Cancelamento de NFS-e";

            var hasSignature = pedRegOriginal.Elements().Any(e => e.Name.LocalName == "Signature");
            var hasNPedRegEvento = infPedRegOriginal.Elements().Any(e => e.Name.LocalName == "nPedRegEvento");
            var existingCancelEvent = infPedRegOriginal.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "e101101" || e.Name.LocalName == "e101103");
            var needsEventRewrite = existingCancelEvent != null &&
                !string.Equals(existingCancelEvent.Name.LocalName, normalizedTarget, StringComparison.OrdinalIgnoreCase);

            // Adapta XML canônico do Padrão Nacional (remove nPedRegEvento, aplica CancelEventType configurado, Id PRE+50+6)
            var infPedReg = new XElement(infPedRegOriginal);
            infPedReg.Elements().Where(e => e.Name.LocalName == "nPedRegEvento").Remove();

            var chNfse = infPedReg.Elements().FirstOrDefault(e => e.Name.LocalName == "chNFSe")?.Value.Trim() ?? string.Empty;
            var currentCancelEl = infPedReg.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "e101101" || e.Name.LocalName == "e101103");
            if (currentCancelEl != null)
            {
                var cMotivo = NormalizeCancelReasonCode(currentCancelEl.Elements().FirstOrDefault(e => e.Name.LocalName == "cMotivo")?.Value);
                var xMotivo = currentCancelEl.Elements().FirstOrDefault(e => e.Name.LocalName == "xMotivo")?.Value.Trim() ?? string.Empty;

                if (needsEventRewrite)
                {
                    currentCancelEl.ReplaceWith(new XElement(
                        ns + normalizedTarget,
                        new XElement(ns + "xDesc", targetXDesc),
                        new XElement(ns + "cMotivo", cMotivo),
                        new XElement(ns + "xMotivo", xMotivo)));
                }
                else if (currentCancelEl.Elements().FirstOrDefault(e => e.Name.LocalName == "cMotivo") is XElement cMotivoEl)
                {
                    cMotivoEl.Value = cMotivo;
                }
            }

            var activeEventEl = infPedReg.Elements().FirstOrDefault(e =>
                e.Name.LocalName.StartsWith("e", StringComparison.OrdinalIgnoreCase) &&
                e.Name.LocalName.Length == 7);
            var eventCode = activeEventEl != null ? activeEventEl.Name.LocalName[1..] : normalizedTarget[1..];

            if (!string.IsNullOrWhiteSpace(chNfse))
                infPedReg.SetAttributeValue("Id", $"PRE{chNfse}{eventCode}");

            ApplyNamespaceRecursive(infPedReg, ns);

            var pedReg = new XElement(ns + "pedRegEvento", new XAttribute("versao", version), infPedReg);

            var canReuseSignature =
                hasSignature &&
                !hasNPedRegEvento &&
                !needsEventRewrite &&
                string.Equals(xDoc.Root?.Name.LocalName, "CancelarNfseEnvio", StringComparison.OrdinalIgnoreCase);

            if (canReuseSignature && pedRegOriginal.Elements().FirstOrDefault(e => e.Name.LocalName == "Signature") is XElement sigEl)
                pedReg.Add(new XElement(sigEl));

            var cancelarEnvio = new XElement(ns + "CancelarNfseEnvio", pedReg);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(cancelarEnvio.ToString(SaveOptions.DisableFormatting));

            SignByReferenceIdIfNeeded(doc, certificate, "pedRegEvento", "infPedReg", exclusiveC14n: false);
            return doc.DocumentElement!;
        }

        private static string NormalizeCancelReasonCode(string? code)
        {
            var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits is "1" or "2" or "9" ? digits : "9";
        }

        private static void ApplyNamespaceRecursive(XElement element, XNamespace ns)
        {
            element.Name = ns + element.Name.LocalName;
            element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
            foreach (var child in element.Elements())
                ApplyNamespaceRecursive(child, ns);
        }

        private static string? ExtractTagValue(XmlDocument xml, string path)
        {
            var parts = path.Split('/');
            var xpath = string.Join("/", parts.Select(part => $"*[local-name()='{part}']"));
            var value = xml.SelectSingleNode($"//{xpath}")?.InnerText?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static XmlElement PrepareDpsGerarNfseEnvio(XmlDocument source, X509Certificate2 certificate, string nfseNamespace)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            var sourceRoot = source.DocumentElement!;

            if (sourceRoot.LocalName == "GerarNfseEnvio")
            {
                doc.LoadXml(source.OuterXml);
                EnsureNamespace(doc.DocumentElement!, nfseNamespace);
            }
            else if (sourceRoot.LocalName == "DPS")
            {
                var gerar = doc.CreateElement("GerarNfseEnvio", nfseNamespace);
                doc.AppendChild(gerar);
                gerar.AppendChild(doc.ImportNode(sourceRoot, true));
            }
            else
            {
                var gerar = doc.CreateElement("GerarNfseEnvio", nfseNamespace);
                doc.AppendChild(gerar);
                var dps = doc.CreateElement("DPS", nfseNamespace);
                dps.SetAttribute("versao", "1.01");
                gerar.AppendChild(dps);
                dps.AppendChild(doc.ImportNode(sourceRoot, true));
            }

            if (doc.SelectSingleNode("//*[local-name()='DPS']") is XmlElement dpsEl &&
                string.IsNullOrWhiteSpace(dpsEl.GetAttribute("versao")))
            {
                dpsEl.SetAttribute("versao", "1.01");
            }

            SignDpsIfNeeded(doc, certificate);
            return doc.DocumentElement!;
        }

        private static void SignDpsIfNeeded(XmlDocument doc, X509Certificate2 certificate)
        {
            if (doc.SelectSingleNode("//*[local-name()='DPS']/*[local-name()='Signature']") != null)
                return;

            var infDps = doc.SelectSingleNode("//*[local-name()='infDPS']") as XmlElement
                ?? throw new InvalidOperationException("Tag infDPS não encontrada para assinatura.");

            var id = infDps.GetAttribute("Id");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("A tag infDPS precisa possuir o atributo Id para assinatura.");

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

            var dpsNode = infDps.ParentNode
                ?? throw new InvalidOperationException("Estrutura de DPS inválida: nó DPS não encontrado.");

            dpsNode.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static XmlElement PrepareGerarNfseEnvio(XmlDocument source, X509Certificate2 certificate, string nfseNamespace)
        {
            var doc = Clone(source);
            var root = doc.DocumentElement!;

            if (root.LocalName == "GerarNfseEnvio")
            {
                EnsureNamespace(root, nfseNamespace);
                UnwrapDeclaracaoPrestacaoServicoIfNeeded(doc);
                SignRpsIfNeeded(doc, certificate);
                return root;
            }

            var wrapper = new XmlDocument { PreserveWhitespace = true };
            var gerar = wrapper.CreateElement("GerarNfseEnvio", nfseNamespace);
            wrapper.AppendChild(gerar);

            if (root.LocalName == "Rps")
            {
                gerar.AppendChild(wrapper.ImportNode(root, true));
            }
            else if (root.LocalName == "DeclaracaoPrestacaoServico")
            {
                var rps = wrapper.CreateElement("Rps", nfseNamespace);
                foreach (XmlNode child in root.ChildNodes)
                {
                    rps.AppendChild(wrapper.ImportNode(child, true));
                }
                gerar.AppendChild(rps);
            }
            else
            {
                var rps = wrapper.CreateElement("Rps", nfseNamespace);
                rps.AppendChild(wrapper.ImportNode(root, true));
                gerar.AppendChild(rps);
            }

            UnwrapDeclaracaoPrestacaoServicoIfNeeded(wrapper);
            SignRpsIfNeeded(wrapper, certificate);
            return wrapper.DocumentElement!;
        }

        private static void UnwrapDeclaracaoPrestacaoServicoIfNeeded(XmlDocument doc)
        {
            var declaracao = doc.SelectSingleNode("//*[local-name()='Rps']/*[local-name()='DeclaracaoPrestacaoServico']") as XmlElement;
            if (declaracao?.ParentNode is XmlElement rps)
            {
                while (declaracao.FirstChild != null)
                {
                    rps.InsertBefore(declaracao.FirstChild, declaracao);
                }
                rps.RemoveChild(declaracao);
            }
        }

        private static XmlDocument Clone(XmlDocument source)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(source.OuterXml);
            return doc;
        }

        private static void EnsureNamespace(XmlElement root, string nfseNamespace)
        {
            if (string.IsNullOrWhiteSpace(root.NamespaceURI))
                root.SetAttribute("xmlns", nfseNamespace);
        }

        private static void SignRpsIfNeeded(XmlDocument doc, X509Certificate2 certificate)
        {
            if (doc.SelectSingleNode("//*[local-name()='Rps']/*[local-name()='Signature']") != null ||
                doc.SelectSingleNode("//*[local-name()='DeclaracaoPrestacaoServico']/*[local-name()='Signature']") != null)
                return;

            var infDeclaracao = doc.SelectSingleNode("//*[local-name()='InfDeclaracaoPrestacaoServico']") as XmlElement;
            if (infDeclaracao == null)
                throw new InvalidOperationException("Não foi encontrada a tag InfDeclaracaoPrestacaoServico para assinatura do RPS.");

            var id = infDeclaracao.GetAttribute("Id");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("A tag InfDeclaracaoPrestacaoServico precisa possuir o atributo Id para assinatura.");

            var signedXml = new SignedXml(doc)
            {
                SigningKey = certificate.GetRSAPrivateKey()
            };

            if (signedXml.SigningKey == null)
                throw new InvalidOperationException("Certificado inválido: não possui chave privada RSA.");

            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            var reference = new Reference($"#{id}")
            {
                DigestMethod = SignedXml.XmlDsigSHA1Url
            };
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(reference);

            signedXml.KeyInfo = new KeyInfo();
            signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.ComputeSignature();

            var declaracao = infDeclaracao.ParentNode;
            if (declaracao == null)
                throw new InvalidOperationException("Estrutura de RPS inválida: DeclaracaoPrestacaoServico não encontrada.");

            declaracao.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static void SignByReferenceIdIfNeeded(
            XmlDocument doc,
            X509Certificate2 certificate,
            string signatureParentLocalName,
            string targetLocalName,
            bool exclusiveC14n)
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

            var signedXml = new SignedXml(doc) { SigningKey = signingKey };
            signedXml.SignedInfo.CanonicalizationMethod = exclusiveC14n
                ? SignedXml.XmlDsigExcC14NTransformUrl
                : SignedXml.XmlDsigC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            var reference = new Reference($"#{id}") { DigestMethod = SignedXml.XmlDsigSHA1Url };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(exclusiveC14n ? new XmlDsigExcC14NTransform() : new XmlDsigC14NTransform());
            signedXml.AddReference(reference);

            signedXml.KeyInfo = new KeyInfo();
            signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.ComputeSignature();

            var parentNode = targetEl.ParentNode
                ?? throw new InvalidOperationException($"Nó pai de {targetLocalName} não encontrado para anexar assinatura.");
            parentNode.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static string CreateSoapEnvelope(
            string nfseDadosXml,
            string nfseNamespace,
            string soapNamespace,
            string version,
            string soapMethod = "GerarNfse")
        {
            var cabecalho = $"<cabecalho versao=\"{version}\" xmlns=\"{nfseNamespace}\"><versaoDados>{version}</versaoDados></cabecalho>";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <{soapMethod} xmlns=""{soapNamespace}"">
      <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
      <nfseDadosMsg>{nfseDadosXml}</nfseDadosMsg>
    </{soapMethod}>
  </soap:Body>
</soap:Envelope>";
        }

        private static async Task<string> SendSoapRequestAsync(X509Certificate2 certificate, string endpoint, string soapEnvelope, string soapAction)
        {
            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            using var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
            {
                CharSet = Encoding.UTF8.WebName
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation("SOAPAction", soapAction);

            using var response = await client.PostAsync(endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }
    }
}
