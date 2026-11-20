using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace application.services.Controllers.DFe.MDFe.Providers
{
    public sealed class SefazMdfeProvider
    {
        private const string MdfeNamespace = "http://www.portalfiscal.inf.br/mdfe";
        private const string MdfeSincNamespace = "http://www.portalfiscal.inf.br/mdfe/wsdl/MDFeRecepcaoSinc";
        private const string MdfeEventoNamespace = "http://www.portalfiscal.inf.br/mdfe/wsdl/MDFeRecepcaoEvento";
        private const string MdfeConsNaoEncNamespace = "http://www.portalfiscal.inf.br/mdfe/wsdl/MDFeConsNaoEnc";

        private static readonly IReadOnlyDictionary<string, string> UfToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RO"] = "11", ["AC"] = "12", ["AM"] = "13", ["RR"] = "14", ["PA"] = "15", ["AP"] = "16", ["TO"] = "17",
            ["MA"] = "21", ["PI"] = "22", ["CE"] = "23", ["RN"] = "24", ["PB"] = "25", ["PE"] = "26", ["AL"] = "27", ["SE"] = "28", ["BA"] = "29",
            ["MG"] = "31", ["ES"] = "32", ["RJ"] = "33", ["SP"] = "35",
            ["PR"] = "41", ["SC"] = "42", ["RS"] = "43",
            ["MS"] = "50", ["MT"] = "51", ["GO"] = "52", ["DF"] = "53",
            ["AN"] = "91"
        };

        private readonly MdfeProvidersXmlStore configStore;

        public SefazMdfeProvider(MdfeProvidersXmlStore configStore)
        {
            this.configStore = configStore;
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument body, string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de MDF-e vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);
            application.services.Extensions.RemoverCaracteresDeEdicao(body.DocumentElement);

            var ns = new XmlNamespaceManager(body.NameTable);
            ns.AddNamespace("m", MdfeNamespace);

            var ide = body.SelectSingleNode("//m:ide", ns) as XmlElement;
            var emit = body.SelectSingleNode("//m:emit", ns) as XmlElement;
            var infMDFe = body.SelectSingleNode("//m:infMDFe", ns) as XmlElement;

            if (ide == null || emit == null || infMDFe == null)
                throw new InvalidOperationException("XML inválido: tags ide, emit ou infMDFe não encontradas.");

            var tpAmbText = ide.SelectSingleNode("m:tpAmb", ns)?.InnerText?.Trim() ?? "2";
            var useHomologation = tpAmbText == "2";
            var cUF = ide.SelectSingleNode("m:cUF", ns)?.InnerText?.Trim() ?? string.Empty;
            var tpEmis = int.Parse(ide.SelectSingleNode("m:tpEmis", ns)?.InnerText?.Trim() ?? "1");
            var mod = int.Parse(ide.SelectSingleNode("m:mod", ns)?.InnerText?.Trim() ?? "58");
            var serie = int.Parse(ide.SelectSingleNode("m:serie", ns)?.InnerText?.Trim() ?? "1");
            var nMDF = long.Parse(ide.SelectSingleNode("m:nMDF", ns)?.InnerText?.Trim() ?? "1");
            var cMDF = int.Parse(ide.SelectSingleNode("m:cMDF", ns)?.InnerText?.Trim() ?? "1");
            var dhEmi = DateTime.Parse(ide.SelectSingleNode("m:dhEmi", ns)?.InnerText?.Trim() ?? DateTime.Now.ToString("O"));
            var docEmit = emit.SelectSingleNode("m:CNPJ", ns)?.InnerText?.Trim()
                ?? emit.SelectSingleNode("m:CPF", ns)?.InnerText?.Trim()
                ?? string.Empty;
            var versao = infMDFe.GetAttribute("versao");
            if (string.IsNullOrWhiteSpace(versao))
                versao = "3.00";

            if (useHomologation)
            {
                var emitNome = emit.SelectSingleNode("m:xNome", ns);
                if (emitNome != null)
                    emitNome.InnerText = "MDF-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
            }

            var (chave, dv) = CalculateAccessKey(int.Parse(ResolveCufCode(cUF)), dhEmi, docEmit, mod, serie, nMDF, tpEmis, cMDF);
            infMDFe.SetAttribute("Id", $"MDFe{chave}");

            var cDvNode = ide.SelectSingleNode("m:cDV", ns);
            if (cDvNode != null)
                cDvNode.InnerText = dv;

            var endpoints = configStore.GetConfig().ResolveEndpoints(cUF, useHomologation, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.MDFeRecepcaoSinc))
                throw new InvalidOperationException($"Endpoint MDFeRecepcaoSinc não configurado para a UF {cUF}.");

            // Atualiza ou insere o QR Code (infMDFeSupl/qrCodMDFe)
            var qrCodeUrlBase = !string.IsNullOrWhiteSpace(endpoints.QrCode)
                ? endpoints.QrCode
                : "https://dfe-portal.svrs.rs.gov.br/mdfe/qrCode";

            var qrCodeUrl = $"{qrCodeUrlBase}?chMDFe={chave}&tpAmb={tpAmbText}";

            var infMDFeSupl = body.SelectSingleNode("//m:infMDFeSupl", ns) as XmlElement;
            if (infMDFeSupl != null)
            {
                var qrNode = infMDFeSupl.SelectSingleNode("m:qrCodMDFe", ns) as XmlElement;
                if (qrNode != null)
                    qrNode.InnerText = qrCodeUrl;
                else
                {
                    var el = body.CreateElement("qrCodMDFe", MdfeNamespace);
                    el.InnerText = qrCodeUrl;
                    infMDFeSupl.AppendChild(el);
                }
            }
            else
            {
                var mdfeEl = body.SelectSingleNode("//m:MDFe", ns) as XmlElement ?? body.DocumentElement!;
                var suplEl = body.CreateElement("infMDFeSupl", MdfeNamespace);
                var qrEl = body.CreateElement("qrCodMDFe", MdfeNamespace);
                qrEl.InnerText = qrCodeUrl;
                suplEl.AppendChild(qrEl);
                mdfeEl.AppendChild(suplEl);
            }

            // Assina infMDFe
            SignElementById(body, certificate, infMDFe, $"MDFe{chave}");

            // Garante que a assinatura fique no final do MDFe
            var mdfeNode = body.SelectSingleNode("//m:MDFe", ns) as XmlElement ?? body.DocumentElement!;
            var signatureNode = mdfeNode.SelectSingleNode("*[local-name()='Signature']");
            if (signatureNode != null)
            {
                mdfeNode.AppendChild(signatureNode);
            }

            var mdfeOuterXml = mdfeNode.OuterXml;
            if (!mdfeOuterXml.Contains("xmlns="))
                mdfeOuterXml = mdfeOuterXml.Replace("<MDFe>", $"<MDFe xmlns=\"{MdfeNamespace}\">");

            var gzipBase64 = CompressGzipBase64(mdfeOuterXml);

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><mdfeDadosMsg xmlns=\"{MdfeSincNamespace}\">{gzipBase64}</mdfeDadosMsg></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.MDFeRecepcaoSinc,
                soapEnvelope,
                $"{MdfeSincNamespace}");

            var resultNode = ExtractSoapResultChild(soapResponse, "mdfeRecepcaoSincResult")
                ?? ExtractSoapResultChild(soapResponse, "retMDFe")
                ?? ExtractSoapResultChild(soapResponse, "retEnviMDFe");

            return BuildMdfeProcXml(body, mdfeNode, resultNode, versao, chave, cUF, tpAmbText);
        }

        public async Task<string> CancelAsync(
            X509Certificate2 certificate,
            string chMDFe,
            string nProt,
            string xJust,
            string cnpjCpf,
            int tpAmb = 1,
            int? cUF = null,
            int nSeqEvento = 1,
            string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chMDFe) || chMDFe.Trim().Length < 44)
                throw new InvalidOperationException("Chave do MDF-e (chMDFe) de 44 dígitos é obrigatória.");
            if (string.IsNullOrWhiteSpace(nProt))
                throw new InvalidOperationException("Protocolo de autorização (nProt) é obrigatório.");
            if (string.IsNullOrWhiteSpace(xJust) || xJust.Trim().Length < 15)
                throw new InvalidOperationException("A justificativa de cancelamento (xJust) deve conter no mínimo 15 caracteres.");

            var cleanKey = chMDFe.Trim();
            var resolvedCuf = cUF?.ToString("D2") ?? cleanKey[..2];
            var cleanDoc = new string((cnpjCpf ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(cleanDoc))
                cleanDoc = cleanKey.Substring(6, 14);

            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";
            var idEvento = $"ID110111{cleanKey}{nSeqEvento:D2}";
            var dhEvento = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var sanitizedJust = global::XmlSanitizer.RemoverAcentos(xJust.Trim());

            var eventoXml = $@"<eventoMDFe xmlns=""{MdfeNamespace}"" versao=""3.00""><infEvento Id=""{idEvento}""><cOrgao>{resolvedCuf}</cOrgao><tpAmb>{tpAmb}</tpAmb>{docTag}<chMDFe>{cleanKey}</chMDFe><dhEvento>{dhEvento}</dhEvento><tpEvento>110111</tpEvento><nSeqEvento>{nSeqEvento}</nSeqEvento><detEvento versaoEvento=""3.00""><evCancMDFe><descEvento>Cancelamento</descEvento><nProt>{nProt.Trim()}</nProt><xJust>{sanitizedJust}</xJust></evCancMDFe></detEvento></infEvento></eventoMDFe>";

            var eventoDoc = new XmlDocument { PreserveWhitespace = true };
            eventoDoc.LoadXml(eventoXml);

            var infEventoEl = (XmlElement)eventoDoc.SelectSingleNode("//*[local-name()='infEvento']")!;
            SignElementById(eventoDoc, certificate, infEventoEl, idEvento);

            var endpoints = configStore.GetConfig().ResolveEndpoints(resolvedCuf, tpAmb == 2, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.MDFeRecepcaoEvento))
                throw new InvalidOperationException($"Endpoint MDFeRecepcaoEvento não configurado para a UF {resolvedCuf}.");

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><mdfeDadosMsg xmlns=\"{MdfeEventoNamespace}\">{eventoDoc.DocumentElement!.OuterXml}</mdfeDadosMsg></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.MDFeRecepcaoEvento,
                soapEnvelope,
                $"{MdfeEventoNamespace}");

            var resultNode = ExtractSoapResultChild(soapResponse, "mdfeRecepcaoEventoResult")
                ?? ExtractSoapResultChild(soapResponse, "retEventoMDFe");

            return resultNode?.OuterXml ?? soapResponse;
        }

        public async Task<string> CloseAsync(
            X509Certificate2 certificate,
            string chMDFe,
            string nProt,
            DateTime? dtEnc,
            int cUFEncerramento,
            long cMunEncerramento,
            string cnpjCpf,
            int tpAmb = 1,
            int? cUF = null,
            int nSeqEvento = 1,
            string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chMDFe) || chMDFe.Trim().Length < 44)
                throw new InvalidOperationException("Chave do MDF-e (chMDFe) de 44 dígitos é obrigatória.");
            if (string.IsNullOrWhiteSpace(nProt))
                throw new InvalidOperationException("Protocolo de autorização (nProt) é obrigatório.");

            var cleanKey = chMDFe.Trim();
            var resolvedCuf = cUF?.ToString("D2") ?? cleanKey[..2];
            var cleanDoc = new string((cnpjCpf ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(cleanDoc))
                cleanDoc = cleanKey.Substring(6, 14);

            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";
            var idEvento = $"ID110112{cleanKey}{nSeqEvento:D2}";
            var dhEvento = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var dtEncFormatted = (dtEnc ?? DateTime.Now).ToString("yyyy-MM-dd");

            var eventoXml = $@"<eventoMDFe xmlns=""{MdfeNamespace}"" versao=""3.00""><infEvento Id=""{idEvento}""><cOrgao>{resolvedCuf}</cOrgao><tpAmb>{tpAmb}</tpAmb>{docTag}<chMDFe>{cleanKey}</chMDFe><dhEvento>{dhEvento}</dhEvento><tpEvento>110112</tpEvento><nSeqEvento>{nSeqEvento}</nSeqEvento><detEvento versaoEvento=""3.00""><evEncMDFe><descEvento>Encerramento</descEvento><nProt>{nProt.Trim()}</nProt><dtEnc>{dtEncFormatted}</dtEnc><cUF>{cUFEncerramento:D2}</cUF><cMun>{cMunEncerramento}</cMun></evEncMDFe></detEvento></infEvento></eventoMDFe>";

            var eventoDoc = new XmlDocument { PreserveWhitespace = true };
            eventoDoc.LoadXml(eventoXml);

            var infEventoEl = (XmlElement)eventoDoc.SelectSingleNode("//*[local-name()='infEvento']")!;
            SignElementById(eventoDoc, certificate, infEventoEl, idEvento);

            var endpoints = configStore.GetConfig().ResolveEndpoints(resolvedCuf, tpAmb == 2, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.MDFeRecepcaoEvento))
                throw new InvalidOperationException($"Endpoint MDFeRecepcaoEvento não configurado para a UF {resolvedCuf}.");

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><mdfeDadosMsg xmlns=\"{MdfeEventoNamespace}\">{eventoDoc.DocumentElement!.OuterXml}</mdfeDadosMsg></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.MDFeRecepcaoEvento,
                soapEnvelope,
                $"{MdfeEventoNamespace}");

            var resultNode = ExtractSoapResultChild(soapResponse, "mdfeRecepcaoEventoResult")
                ?? ExtractSoapResultChild(soapResponse, "retEventoMDFe");

            return resultNode?.OuterXml ?? soapResponse;
        }

        public async Task<string> ConsultNonClosedAsync(
            X509Certificate2 certificate,
            string cnpjCpf,
            int? cUF = null,
            int tpAmb = 1,
            string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            var cleanDoc = new string((cnpjCpf ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(cleanDoc))
                throw new InvalidOperationException("CNPJ/CPF é obrigatório para consulta de MDF-e não encerrados.");

            var resolvedCuf = cUF?.ToString("D2") ?? "43";
            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";

            var consXml = $@"<consMDFeNaoEnc xmlns=""{MdfeNamespace}"" versao=""3.00""><tpAmb>{tpAmb}</tpAmb><xServ>CONSULTAR NAO ENCERRADOS</xServ>{docTag}</consMDFeNaoEnc>";

            var endpoints = configStore.GetConfig().ResolveEndpoints(resolvedCuf, tpAmb == 2, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.MDFeConsNaoEnc))
                throw new InvalidOperationException($"Endpoint MDFeConsNaoEnc não configurado.");

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><mdfeDadosMsg xmlns=\"{MdfeConsNaoEncNamespace}\">{consXml}</mdfeDadosMsg></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.MDFeConsNaoEnc,
                soapEnvelope,
                $"{MdfeConsNaoEncNamespace}");

            var resultNode = ExtractSoapResultChild(soapResponse, "mdfeConsNaoEncResult")
                ?? ExtractSoapResultChild(soapResponse, "retConsMDFeNaoEnc");

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

        private static string BuildMdfeProcXml(XmlDocument body, XmlElement mdfeOriginal, XmlNode? resultadoNode, string versao, string chave, string cUF, string tpAmb)
        {
            var retornoDoc = new XmlDocument { PreserveWhitespace = false };
            var rawResultXml = resultadoNode?.OuterXml ?? "<retMDFe xmlns=\"http://www.portalfiscal.inf.br/mdfe\" />";
            retornoDoc.LoadXml($"<retornoWrapper xmlns=\"{MdfeNamespace}\">{rawResultXml}</retornoWrapper>");

            var nsRet = new XmlNamespaceManager(retornoDoc.NameTable);
            nsRet.AddNamespace("m", MdfeNamespace);

            var protMdfeNode = retornoDoc.SelectSingleNode("//m:protMDFe", nsRet);

            var mdfeProc = new XmlDocument { PreserveWhitespace = true };
            mdfeProc.LoadXml($@"<mdfeProc xmlns=""{MdfeNamespace}"" versao=""{versao}"" />");
            var root = mdfeProc.DocumentElement!;

            root.AppendChild(mdfeProc.ImportNode(mdfeOriginal, true));

            if (protMdfeNode != null)
            {
                root.AppendChild(mdfeProc.ImportNode(protMdfeNode, true));
            }
            else
            {
                var protMDFe = mdfeProc.CreateElement("protMDFe", MdfeNamespace);
                protMDFe.SetAttribute("versao", versao);

                var infProt = mdfeProc.CreateElement("infProt", MdfeNamespace);
                infProt.SetAttribute("Id", $"ID{chave}");

                void AppendField(string field, string fallback = "")
                {
                    var found = retornoDoc.SelectSingleNode($"//m:{field}", nsRet)?.InnerText?.Trim();
                    var val = !string.IsNullOrWhiteSpace(found) ? found : fallback;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var el = mdfeProc.CreateElement(field, MdfeNamespace);
                        el.InnerText = val;
                        infProt.AppendChild(el);
                    }
                }

                AppendField("tpAmb", tpAmb);
                AppendField("verAplic", "SPED_DFE_1.0");
                AppendField("chMDFe", chave);
                AppendField("dhRecbto", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
                AppendField("nProt", "");
                AppendField("digVal", "");
                AppendField("cStat", "999");
                AppendField("xMotivo", "Erro no processamento da SEFAZ");

                protMDFe.AppendChild(infProt);
                root.AppendChild(protMDFe);
            }

            return mdfeProc.OuterXml;
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
            var trimmed = xml.TrimStart('\uFEFF').Trim();
            if (!trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                trimmed = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + trimmed;

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(trimmed);
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

            using var content = new StringContent(soapEnvelope, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{actionUrn}\"");

            using var response = await client.PostAsync(endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }
    }
}
