using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace application.services.Controllers.DFe.CTe.Providers
{
    public sealed class SefazCteProvider
    {
        private const string CteNamespace = "http://www.portalfiscal.inf.br/cte";
        private const string CteSincNamespace = "http://www.portalfiscal.inf.br/cte/wsdl/CTeRecepcaoSincV4";
        private const string CteEventoNamespace = "http://www.portalfiscal.inf.br/cte/wsdl/CTeRecepcaoEventoV4";
        private const string CteDistNamespace = "http://www.portalfiscal.inf.br/cte/wsdl/CTeDistribuicaoDFe";

        private static readonly IReadOnlyDictionary<string, string> UfToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RO"] = "11", ["AC"] = "12", ["AM"] = "13", ["RR"] = "14", ["PA"] = "15", ["AP"] = "16", ["TO"] = "17",
            ["MA"] = "21", ["PI"] = "22", ["CE"] = "23", ["RN"] = "24", ["PB"] = "25", ["PE"] = "26", ["AL"] = "27", ["SE"] = "28", ["BA"] = "29",
            ["MG"] = "31", ["ES"] = "32", ["RJ"] = "33", ["SP"] = "35",
            ["PR"] = "41", ["SC"] = "42", ["RS"] = "43",
            ["MS"] = "50", ["MT"] = "51", ["GO"] = "52", ["DF"] = "53",
            ["AN"] = "91"
        };

        private readonly CteProvidersXmlStore configStore;

        public SefazCteProvider(CteProvidersXmlStore configStore)
        {
            this.configStore = configStore;
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument body, string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de CT-e vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);
            application.services.Extensions.RemoverCaracteresDeEdicao(body.DocumentElement);

            var ns = new XmlNamespaceManager(body.NameTable);
            ns.AddNamespace("c", CteNamespace);

            var ide = body.SelectSingleNode("//c:ide", ns) as XmlElement;
            var emit = body.SelectSingleNode("//c:emit", ns) as XmlElement;
            var infCte = body.SelectSingleNode("//c:infCte", ns) as XmlElement;

            if (ide == null || emit == null || infCte == null)
                throw new InvalidOperationException("XML inválido: tags ide, emit ou infCte não encontradas.");

            var tpAmbText = ide.SelectSingleNode("c:tpAmb", ns)?.InnerText?.Trim() ?? "2";
            var useHomologation = tpAmbText == "2";
            var cUF = ide.SelectSingleNode("c:cUF", ns)?.InnerText?.Trim() ?? string.Empty;
            var tpEmis = int.Parse(ide.SelectSingleNode("c:tpEmis", ns)?.InnerText?.Trim() ?? "1");
            var mod = int.Parse(ide.SelectSingleNode("c:mod", ns)?.InnerText?.Trim() ?? "57");
            var serie = int.Parse(ide.SelectSingleNode("c:serie", ns)?.InnerText?.Trim() ?? "1");
            var nCT = long.Parse(ide.SelectSingleNode("c:nCT", ns)?.InnerText?.Trim() ?? "1");
            var cCT = int.Parse(ide.SelectSingleNode("c:cCT", ns)?.InnerText?.Trim() ?? "1");
            var dhEmi = DateTime.Parse(ide.SelectSingleNode("c:dhEmi", ns)?.InnerText?.Trim() ?? DateTime.Now.ToString("O"));
            var docEmit = emit.SelectSingleNode("c:CNPJ", ns)?.InnerText?.Trim()
                ?? emit.SelectSingleNode("c:CPF", ns)?.InnerText?.Trim()
                ?? string.Empty;
            var versao = infCte.GetAttribute("versao");
            if (string.IsNullOrWhiteSpace(versao))
                versao = "4.00";

            if (useHomologation)
            {
                var remNome = body.SelectSingleNode("//c:rem/c:xNome", ns);
                if (remNome != null)
                    remNome.InnerText = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

                var destNome = body.SelectSingleNode("//c:dest/c:xNome", ns);
                if (destNome != null)
                    destNome.InnerText = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
            }

            var (chave, dv) = CalculateAccessKey(int.Parse(ResolveCufCode(cUF)), dhEmi, docEmit, mod, serie, nCT, tpEmis, cCT);
            infCte.SetAttribute("Id", $"CTe{chave}");

            var cDvNode = ide.SelectSingleNode("c:cDV", ns);
            if (cDvNode != null)
                cDvNode.InnerText = dv;

            var endpoints = configStore.GetConfig().ResolveEndpoints(cUF, useHomologation, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.CTeRecepcaoSinc))
                throw new InvalidOperationException($"Endpoint CTeRecepcaoSinc não configurado para a UF {cUF}.");

            // Atualiza ou insere o QR Code (infCTeSupl/qrCodCTe)
            var qrCodeUrlBase = !string.IsNullOrWhiteSpace(endpoints.QrCode)
                ? endpoints.QrCode
                : "https://dfe-portal.svrs.rs.gov.br/cte/qrCode";

            var qrCodeUrl = $"{qrCodeUrlBase}?chCTe={chave}&tpAmb={tpAmbText}";

            var infCTeSupl = body.SelectSingleNode("//c:infCTeSupl", ns) as XmlElement;
            if (infCTeSupl != null)
            {
                var qrNode = infCTeSupl.SelectSingleNode("c:qrCodCTe", ns) as XmlElement;
                if (qrNode != null)
                    qrNode.InnerText = qrCodeUrl;
                else
                {
                    var el = body.CreateElement("qrCodCTe", CteNamespace);
                    el.InnerText = qrCodeUrl;
                    infCTeSupl.AppendChild(el);
                }
            }
            else
            {
                var cteEl = body.SelectSingleNode("//c:CTe", ns) as XmlElement ?? body.DocumentElement!;
                var suplEl = body.CreateElement("infCTeSupl", CteNamespace);
                var qrEl = body.CreateElement("qrCodCTe", CteNamespace);
                qrEl.InnerText = qrCodeUrl;
                suplEl.AppendChild(qrEl);
                cteEl.AppendChild(suplEl);
            }

            // Assina infCte
            SignElementById(body, certificate, infCte, $"CTe{chave}");

            // Garante que a assinatura fique no final do CTe
            var cteNode = body.SelectSingleNode("//c:CTe", ns) as XmlElement ?? body.DocumentElement!;
            var signatureNode = cteNode.SelectSingleNode("*[local-name()='Signature']");
            if (signatureNode != null)
            {
                cteNode.AppendChild(signatureNode);
            }

            var cteOuterXml = cteNode.OuterXml;
            if (!cteOuterXml.Contains("xmlns="))
                cteOuterXml = cteOuterXml.Replace("<CTe>", $"<CTe xmlns=\"{CteNamespace}\">");

            var gzipBase64 = CompressGzipBase64(cteOuterXml);

            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap12:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap12=""http://www.w3.org/2003/05/soap-envelope"">
  <soap12:Body>
    <cteDadosMsg xmlns=""{CteSincNamespace}"">{gzipBase64}</cteDadosMsg>
  </soap12:Body>
</soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.CTeRecepcaoSinc,
                soapEnvelope,
                $"{CteSincNamespace}/cteRecepcao");

            var resultNode = ExtractSoapResultChild(soapResponse, "cteRecepcaoSincResult")
                ?? ExtractSoapResultChild(soapResponse, "retCTe");

            return BuildCteProcXml(body, cteNode, resultNode, versao, chave, cUF, tpAmbText);
        }

        public async Task<string> CancelAsync(
            X509Certificate2 certificate,
            string chCTe,
            string nProt,
            string xJust,
            string cnpjCpf,
            int tpAmb = 1,
            int? cUF = null,
            int nSeqEvento = 1,
            string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chCTe) || chCTe.Trim().Length < 44)
                throw new InvalidOperationException("Chave do CT-e (chCTe) de 44 dígitos é obrigatória.");
            if (string.IsNullOrWhiteSpace(nProt))
                throw new InvalidOperationException("Protocolo de autorização (nProt) é obrigatório.");
            if (string.IsNullOrWhiteSpace(xJust) || xJust.Trim().Length < 15)
                throw new InvalidOperationException("A justificativa de cancelamento (xJust) deve conter no mínimo 15 caracteres.");

            var cleanKey = chCTe.Trim();
            var resolvedCuf = cUF?.ToString("D2") ?? cleanKey[..2];
            var cleanDoc = new string((cnpjCpf ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(cleanDoc))
                cleanDoc = cleanKey.Substring(6, 14);

            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";
            // No CT-e 4.00 o atributo Id de <infEvento> usa nSeqEvento com 3 dígitos (total 55 caracteres: ID + 6 + 44 + 3)
            var idEvento = $"ID110111{cleanKey}{nSeqEvento:D3}";
            var dhEvento = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var sanitizedJust = global::XmlSanitizer.RemoverAcentos(xJust.Trim());

            var eventoXml = $@"<eventoCTe xmlns=""{CteNamespace}"" versao=""4.00""><infEvento Id=""{idEvento}""><cOrgao>{resolvedCuf}</cOrgao><tpAmb>{tpAmb}</tpAmb>{docTag}<chCTe>{cleanKey}</chCTe><dhEvento>{dhEvento}</dhEvento><tpEvento>110111</tpEvento><nSeqEvento>{nSeqEvento}</nSeqEvento><detEvento versaoEvento=""4.00""><evCancCTe><descEvento>Cancelamento</descEvento><nProt>{nProt.Trim()}</nProt><xJust>{sanitizedJust}</xJust></evCancCTe></detEvento></infEvento></eventoCTe>";

            var eventoDoc = new XmlDocument { PreserveWhitespace = true };
            eventoDoc.LoadXml(eventoXml);

            var infEventoEl = (XmlElement)eventoDoc.SelectSingleNode("//*[local-name()='infEvento']")!;
            SignElementById(eventoDoc, certificate, infEventoEl, idEvento);

            var endpoints = configStore.GetConfig().ResolveEndpoints(resolvedCuf, tpAmb == 2, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.CTeRecepcaoEvento))
                throw new InvalidOperationException($"Endpoint CTeRecepcaoEvento não configurado para a UF {resolvedCuf}.");

            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap12:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap12=""http://www.w3.org/2003/05/soap-envelope"">
  <soap12:Body>
    <cteDadosMsg xmlns=""{CteEventoNamespace}"">{eventoDoc.DocumentElement!.OuterXml}</cteDadosMsg>
  </soap12:Body>
</soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.CTeRecepcaoEvento,
                soapEnvelope,
                $"{CteEventoNamespace}/cteRecepcaoEvento");

            var resultNode = ExtractSoapResultChild(soapResponse, "cteRecepcaoEventoResult")
                ?? ExtractSoapResultChild(soapResponse, "retEventoCTe");

            return resultNode?.OuterXml ?? soapResponse;
        }

        public async Task<string> DistribuitionAsync(X509Certificate2 certificate, XmlDocument body)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de distribuição vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

            var cUFAutor = body.SelectSingleNode("//*[local-name()='cUFAutor']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='ufAutor']")?.InnerText?.Trim();
            var documento = body.SelectSingleNode("//*[local-name()='documento']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CNPJ']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CPF']")?.InnerText?.Trim();
            var ultNSU = body.SelectSingleNode("//*[local-name()='ultNSU']")?.InnerText?.Trim();
            var nSU = body.SelectSingleNode("//*[local-name()='nSU']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='NSU']")?.InnerText?.Trim();
            var chCTe = body.SelectSingleNode("//*[local-name()='chCTe']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='chCTE']")?.InnerText?.Trim();
            var tpAmb = body.SelectSingleNode("//*[local-name()='tpAmb']")?.InnerText?.Trim() ?? "1";

            if (string.IsNullOrWhiteSpace(cUFAutor) || string.IsNullOrWhiteSpace(documento))
                throw new InvalidOperationException("cUFAutor e documento são obrigatórios.");

            var resolvedCuf = ResolveCufCode(cUFAutor);
            var cleanDoc = new string(documento.Where(char.IsDigit).ToArray());
            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";

            string queryBlock;
            if (!string.IsNullOrWhiteSpace(chCTe))
            {
                queryBlock = $"<consChCTe><chCTe>{chCTe.Trim()}</chCTe></consChCTe>";
            }
            else if (!string.IsNullOrWhiteSpace(nSU) && nSU != "0")
            {
                queryBlock = $"<consNSU><NSU>{nSU.PadLeft(15, '0')}</NSU></consNSU>";
            }
            else
            {
                var paddedUltNsu = (ultNSU ?? "0").PadLeft(15, '0');
                queryBlock = $"<distNSU><ultNSU>{paddedUltNsu}</ultNSU></distNSU>";
            }

            var distDFeIntXml = $"<distDFeInt versao=\"1.00\" xmlns=\"{CteNamespace}\"><tpAmb>{tpAmb}</tpAmb><cUFAutor>{resolvedCuf}</cUFAutor>{docTag}{queryBlock}</distDFeInt>";

            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap12:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap12=""http://www.w3.org/2003/05/soap-envelope"">
  <soap12:Body>
    <cteDistDFeInteresse xmlns=""{CteDistNamespace}"">
      <cteDadosMsg>{distDFeIntXml}</cteDadosMsg>
    </cteDistDFeInteresse>
  </soap12:Body>
</soap12:Envelope>";

            var endpoint = configStore.GetConfig().ResolveDistribuicaoEndpoint(tpAmb == "2");
            var soapResponse = await SendSoap12Async(
                certificate,
                endpoint,
                soapEnvelope,
                $"{CteDistNamespace}/cteDistDFeInteresse");

            var resultNode = ExtractSoapResultChild(soapResponse, "cteDistDFeInteresseResult");
            return resultNode?.OuterXml ?? soapResponse;
        }

        private static string ResolveCufCode(string ufOrCode)
        {
            var trimmed = ufOrCode.Trim();
            if (trimmed.All(char.IsDigit))
                return trimmed.PadLeft(2, '0');

            return UfToCode.TryGetValue(trimmed, out var code) ? code : "43";
        }

        private static (string Chave, string Dv) CalculateAccessKey(
            int ufEmitente,
            DateTime dataEmissao,
            string cnpjCpfEmitente,
            int modelo,
            int serie,
            long numero,
            int tipoEmissao,
            int cDoc)
        {
            var docDigits = new string((cnpjCpfEmitente ?? string.Empty).Where(char.IsDigit).ToArray()).PadLeft(14, '0');
            if (docDigits.Length > 14)
                docDigits = docDigits[^14..];

            var sb = new StringBuilder(44);
            sb.Append(ufEmitente.ToString("D2"))
              .Append(dataEmissao.ToString("yyMM"))
              .Append(docDigits)
              .Append(modelo.ToString("D2"))
              .Append(serie.ToString("D3"))
              .Append(numero.ToString("D9"))
              .Append(tipoEmissao.ToString())
              .Append(cDoc.ToString("D8"));

            var baseKey = sb.ToString();
            var soma = 0;
            var peso = 2;
            for (var i = baseKey.Length - 1; i >= 0; i--)
            {
                soma += (baseKey[i] - '0') * peso;
                peso = peso < 9 ? peso + 1 : 2;
            }

            var mod = soma % 11;
            var dv = (mod == 0 || mod == 1) ? 0 : 11 - mod;
            var dvStr = dv.ToString();

            return (baseKey + dvStr, dvStr);
        }

        private static void SignElementById(XmlDocument doc, X509Certificate2 certificate, XmlElement targetElement, string id)
        {
            var parentNode = targetElement.ParentNode as XmlElement ?? doc.DocumentElement!;
            var existingSignature = parentNode.SelectSingleNode("*[local-name()='Signature']");
            if (existingSignature != null)
                parentNode.RemoveChild(existingSignature);

            var signingKey = certificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Certificado inválido: não possui chave privada RSA.");

            var signedXml = new SignedXml(doc)
            {
                SigningKey = signingKey
            };

            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
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

            parentNode.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static string BuildCteProcXml(XmlDocument body, XmlElement cteOriginal, XmlNode? resultadoNode, string versao, string chave, string cUF, string tpAmb)
        {
            var retornoDoc = new XmlDocument { PreserveWhitespace = false };
            var rawResultXml = resultadoNode?.OuterXml ?? "<retCTe xmlns=\"http://www.portalfiscal.inf.br/cte\" />";
            retornoDoc.LoadXml($"<retornoWrapper xmlns=\"{CteNamespace}\">{rawResultXml}</retornoWrapper>");

            var nsRet = new XmlNamespaceManager(retornoDoc.NameTable);
            nsRet.AddNamespace("c", CteNamespace);

            var protCteNode = retornoDoc.SelectSingleNode("//c:protCTe", nsRet);

            var cteProc = new XmlDocument { PreserveWhitespace = true };
            cteProc.LoadXml($@"<cteProc xmlns=""{CteNamespace}"" versao=""{versao}"" />");
            var root = cteProc.DocumentElement!;

            root.AppendChild(cteProc.ImportNode(cteOriginal, true));

            if (protCteNode != null)
            {
                root.AppendChild(cteProc.ImportNode(protCteNode, true));
            }
            else
            {
                // Constrói protCTe sintetizado se a SEFAZ retornou apenas retCTe com rejeição/status
                var protCTe = cteProc.CreateElement("protCTe", CteNamespace);
                protCTe.SetAttribute("versao", versao);

                var infProt = cteProc.CreateElement("infProt", CteNamespace);
                infProt.SetAttribute("Id", $"ID{chave}");

                void AppendField(string field, string fallback = "")
                {
                    var found = retornoDoc.SelectSingleNode($"//c:{field}", nsRet)?.InnerText?.Trim();
                    var val = !string.IsNullOrWhiteSpace(found) ? found : fallback;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var el = cteProc.CreateElement(field, CteNamespace);
                        el.InnerText = val;
                        infProt.AppendChild(el);
                    }
                }

                AppendField("tpAmb", tpAmb);
                AppendField("verAplic", "SPED_DFE_1.0");
                AppendField("chCTe", chave);
                AppendField("dhRecbto", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
                AppendField("nProt", "");
                AppendField("digVal", "");
                AppendField("cStat", "999");
                AppendField("xMotivo", "Erro no processamento da SEFAZ");

                protCTe.AppendChild(infProt);
                root.AppendChild(protCTe);
            }

            return cteProc.OuterXml;
        }

        private static XmlNode? ExtractSoapResultChild(string soapResponseXml, string responseElementLocalName)
        {
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.LoadXml(soapResponseXml);
            var wrapper = doc.SelectSingleNode($"//*[local-name()='{responseElementLocalName}']");
            return wrapper?.FirstChild ?? wrapper ?? doc.DocumentElement;
        }

        private static string CompressGzipBase64(string xml)
        {
            var bytes = Encoding.UTF8.GetBytes(xml);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray());
        }

        private static async Task<string> SendSoap12Async(
            X509Certificate2 certificate,
            string endpoint,
            string soapEnvelope,
            string actionUrn)
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            handler.ClientCertificates.Add(certificate);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            using var content = new StringContent(soapEnvelope, Encoding.UTF8);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{actionUrn}\"");

            using var response = await client.PostAsync(endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }
    }
}
