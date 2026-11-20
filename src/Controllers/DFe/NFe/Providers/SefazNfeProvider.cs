using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace application.services.Controllers.DFe.NFe.Providers
{
    public sealed class SefazNfeProvider
    {
        private const string NfeNamespace = "http://www.portalfiscal.inf.br/nfe";
        private static readonly IReadOnlyDictionary<string, string> UfToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RO"] = "11", ["AC"] = "12", ["AM"] = "13", ["RR"] = "14", ["PA"] = "15", ["AP"] = "16", ["TO"] = "17",
            ["MA"] = "21", ["PI"] = "22", ["CE"] = "23", ["RN"] = "24", ["PB"] = "25", ["PE"] = "26", ["AL"] = "27", ["SE"] = "28", ["BA"] = "29",
            ["MG"] = "31", ["ES"] = "32", ["RJ"] = "33", ["SP"] = "35",
            ["PR"] = "41", ["SC"] = "42", ["RS"] = "43",
            ["MS"] = "50", ["MT"] = "51", ["GO"] = "52", ["DF"] = "53",
            ["AN"] = "91"
        };

        private readonly NfeProvidersXmlStore configStore;

        public SefazNfeProvider(NfeProvidersXmlStore configStore)
        {
            this.configStore = configStore;
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument body, string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de NF-e vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);
            application.services.Extensions.RemoverCaracteresDeEdicao(body.DocumentElement);

            var ns = new XmlNamespaceManager(body.NameTable);
            ns.AddNamespace("n", NfeNamespace);

            var ide = body.SelectSingleNode("//n:ide", ns) as XmlElement;
            var emit = body.SelectSingleNode("//n:emit", ns) as XmlElement;
            var infNFe = body.SelectSingleNode("//n:infNFe", ns) as XmlElement;

            if (ide == null || emit == null || infNFe == null)
                throw new InvalidOperationException("XML inválido: tags ide, emit ou infNFe não encontradas.");

            var tpAmbText = ide.SelectSingleNode("n:tpAmb", ns)?.InnerText?.Trim() ?? "2";
            var useHomologation = tpAmbText == "2";
            var cUF = ide.SelectSingleNode("n:cUF", ns)?.InnerText?.Trim() ?? string.Empty;
            var tpEmis = int.Parse(ide.SelectSingleNode("n:tpEmis", ns)?.InnerText?.Trim() ?? "1");
            var mod = int.Parse(ide.SelectSingleNode("n:mod", ns)?.InnerText?.Trim() ?? "55");
            var serie = int.Parse(ide.SelectSingleNode("n:serie", ns)?.InnerText?.Trim() ?? "1");
            var nNF = long.Parse(ide.SelectSingleNode("n:nNF", ns)?.InnerText?.Trim() ?? "1");
            var cNF = int.Parse(ide.SelectSingleNode("n:cNF", ns)?.InnerText?.Trim() ?? "1");
            var dhEmi = DateTime.Parse(ide.SelectSingleNode("n:dhEmi", ns)?.InnerText?.Trim() ?? DateTime.Now.ToString("O"));
            var docEmit = emit.SelectSingleNode("n:CNPJ", ns)?.InnerText?.Trim()
                ?? emit.SelectSingleNode("n:CPF", ns)?.InnerText?.Trim()
                ?? string.Empty;
            var versao = infNFe.GetAttribute("versao");
            if (string.IsNullOrWhiteSpace(versao))
                versao = "4.00";

            if (useHomologation)
            {
                var destNome = body.SelectSingleNode("//n:dest/n:xNome", ns);
                if (destNome != null)
                    destNome.InnerText = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
            }

            var (chave, dv) = CalculateAccessKey(int.Parse(ResolveCufCode(cUF)), dhEmi, docEmit, mod, serie, nNF, tpEmis, cNF);
            infNFe.SetAttribute("Id", $"NFe{chave}");

            var cDvNode = ide.SelectSingleNode("n:cDV", ns);
            if (cDvNode != null)
                cDvNode.InnerText = dv;

            SignElementById(body, certificate, infNFe, $"NFe{chave}");

            var nfeNode = body.SelectSingleNode("//n:NFe", ns) as XmlElement ?? body.DocumentElement!;
            var nfeOuterXml = nfeNode.OuterXml;
            if (!nfeOuterXml.Contains("xmlns="))
                nfeOuterXml = nfeOuterXml.Replace("<NFe>", $"<NFe xmlns=\"{NfeNamespace}\">");

            var enviNFeXml = $"<enviNFe xmlns=\"{NfeNamespace}\" versao=\"{versao}\"><idLote>000000000000001</idLote><indSinc>1</indSinc>{nfeOuterXml}</enviNFe>";
            var gzipBase64 = CompressGzipBase64(enviNFeXml);

            var endpoints = configStore.GetConfig().ResolveEndpoints(cUF, useHomologation, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.NFeAutorizacao))
                throw new InvalidOperationException($"Endpoint NFeAutorizacao não configurado para a UF {cUF}.");

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><nfeDadosMsgZip xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4\">{gzipBase64}</nfeDadosMsgZip></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.NFeAutorizacao,
                soapEnvelope,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLoteZIP");

            var resultNode = ExtractSoapResultChild(soapResponse, "nfeResultMsg");
            return BuildNfeProcXml(body, nfeNode, resultNode, versao);
        }

        public async Task<string> DistribuitionAsync(X509Certificate2 certificate, XmlDocument body)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de distribuição vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

            var ufAutor = body.SelectSingleNode("//*[local-name()='ufAutor']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='cUFAutor']")?.InnerText?.Trim();
            var documento = body.SelectSingleNode("//*[local-name()='documento']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CNPJ']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CPF']")?.InnerText?.Trim();
            var ultNSU = body.SelectSingleNode("//*[local-name()='ultNSU']")?.InnerText?.Trim();
            var nSU = body.SelectSingleNode("//*[local-name()='nSU']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='NSU']")?.InnerText?.Trim();
            var chNFE = body.SelectSingleNode("//*[local-name()='chNFE']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='chNFe']")?.InnerText?.Trim();
            var tpAmb = body.SelectSingleNode("//*[local-name()='tpAmb']")?.InnerText?.Trim() ?? "1";

            if (string.IsNullOrWhiteSpace(ufAutor) || string.IsNullOrWhiteSpace(documento))
                throw new InvalidOperationException("ufAutor e documento são obrigatórios.");

            var cUFAutor = ResolveCufCode(ufAutor);
            var cleanDoc = new string(documento.Where(char.IsDigit).ToArray());
            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";

            string queryBlock;
            if (!string.IsNullOrWhiteSpace(chNFE))
            {
                queryBlock = $"<consChNFe><chNFe>{chNFE.Trim()}</chNFe></consChNFe>";
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

            var distDFeIntXml = $"<distDFeInt versao=\"1.00\" xmlns=\"{NfeNamespace}\"><tpAmb>{tpAmb}</tpAmb><cUFAutor>{cUFAutor}</cUFAutor>{docTag}{queryBlock}</distDFeInt>";

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><nfeDistDFeInteresse xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe\"><nfeDadosMsg>{distDFeIntXml}</nfeDadosMsg></nfeDistDFeInteresse></soap12:Body></soap12:Envelope>";

            var endpoint = configStore.GetConfig().ResolveDistribuicaoEndpoint(tpAmb == "2");
            var soapResponse = await SendSoap12Async(
                certificate,
                endpoint,
                soapEnvelope,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe/nfeDistDFeInteresse");

            var resultNode = ExtractSoapResultChild(soapResponse, "nfeDistDFeInteresseResult");
            return resultNode?.OuterXml ?? soapResponse;
        }

        public async Task<string> ManifestationAsync(X509Certificate2 certificate, XmlDocument body)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (body?.DocumentElement == null) throw new InvalidOperationException("XML de manifestação vazio ou inválido.");

            application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

            var documento = body.SelectSingleNode("//*[local-name()='documento']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CNPJ']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='CPF']")?.InnerText?.Trim()
                ?? string.Empty;
            var chNFE = body.SelectSingleNode("//*[local-name()='chNFE']")?.InnerText?.Trim()
                ?? body.SelectSingleNode("//*[local-name()='chNFe']")?.InnerText?.Trim()
                ?? string.Empty;
            var tpAmb = body.SelectSingleNode("//*[local-name()='tpAmb']")?.InnerText?.Trim() ?? "1";
            var tpEvento = body.SelectSingleNode("//*[local-name()='tpEvento']")?.InnerText?.Trim() ?? "210200";
            var nSeqEvento = int.TryParse(body.SelectSingleNode("//*[local-name()='nSeqEvento']")?.InnerText?.Trim(), out var parsedSeq)
                ? parsedSeq
                : 1;

            if (string.IsNullOrWhiteSpace(documento) || string.IsNullOrWhiteSpace(chNFE))
                throw new InvalidOperationException("documento e chNFE são obrigatórios para manifestação.");

            var descEvento = tpEvento switch
            {
                "210200" => "Confirmacao da Operacao",
                "210210" => "Ciencia da Operacao",
                "210220" => "Desconhecimento da Operacao",
                "210240" => "Operacao nao Realizada",
                _ => "Confirmacao da Operacao"
            };

            var cleanDoc = new string(documento.Where(char.IsDigit).ToArray());
            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";
            var idEvento = $"ID{tpEvento}{chNFE}{nSeqEvento:D2}";
            var dhEvento = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

            var eventoXml = $@"<envEvento xmlns=""{NfeNamespace}"" versao=""1.00""><idLote>1</idLote><evento xmlns=""{NfeNamespace}"" versao=""1.00""><infEvento Id=""{idEvento}""><cOrgao>91</cOrgao><tpAmb>{tpAmb}</tpAmb>{docTag}<chNFe>{chNFE}</chNFe><dhEvento>{dhEvento}</dhEvento><tpEvento>{tpEvento}</tpEvento><nSeqEvento>{nSeqEvento}</nSeqEvento><verEvento>1.00</verEvento><detEvento versao=""1.00""><descEvento>{descEvento}</descEvento></detEvento></infEvento></evento></envEvento>";

            var eventoDoc = new XmlDocument { PreserveWhitespace = true };
            eventoDoc.LoadXml(eventoXml);

            var infEventoEl = (XmlElement)eventoDoc.SelectSingleNode("//*[local-name()='infEvento']")!;
            SignElementById(eventoDoc, certificate, infEventoEl, idEvento);

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><nfeDadosMsg xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4\">{eventoDoc.DocumentElement!.OuterXml}</nfeDadosMsg></soap12:Body></soap12:Envelope>";

            var endpoint = configStore.GetConfig().ResolveNationalEventoEndpoint(tpAmb == "2");
            var soapResponse = await SendSoap12Async(
                certificate,
                endpoint,
                soapEnvelope,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEvento");

            var resultNode = ExtractSoapResultChild(soapResponse, "nfeResultMsg");
            return resultNode?.OuterXml ?? soapResponse;
        }

        public async Task<string> CancelAsync(
            X509Certificate2 certificate,
            string chNFe,
            string nProt,
            string xJust,
            string cnpjCpf,
            int tpAmb = 1,
            int? cUF = null,
            int nSeqEvento = 1,
            string? requestedProvider = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrWhiteSpace(chNFe) || chNFe.Trim().Length < 44)
                throw new InvalidOperationException("Chave da NF-e (chNFe) de 44 dígitos é obrigatória.");
            if (string.IsNullOrWhiteSpace(nProt))
                throw new InvalidOperationException("Protocolo de autorização (nProt) é obrigatório.");
            if (string.IsNullOrWhiteSpace(xJust))
                throw new InvalidOperationException("Justificativa de cancelamento (xJust) é obrigatória.");

            var cleanKey = chNFe.Trim();
            var resolvedCuf = cUF?.ToString("D2") ?? cleanKey[..2];
            var cleanDoc = new string((cnpjCpf ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(cleanDoc))
                cleanDoc = cleanKey.Substring(6, 14);

            var docTag = cleanDoc.Length == 11 ? $"<CPF>{cleanDoc}</CPF>" : $"<CNPJ>{cleanDoc}</CNPJ>";
            var idEvento = $"ID110111{cleanKey}{nSeqEvento:D2}";
            var dhEvento = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var sanitizedJust = global::XmlSanitizer.RemoverAcentos(xJust.Trim());

            var eventoXml = $@"<envEvento xmlns=""{NfeNamespace}"" versao=""1.00""><idLote>1</idLote><evento xmlns=""{NfeNamespace}"" versao=""1.00""><infEvento Id=""{idEvento}""><cOrgao>{resolvedCuf}</cOrgao><tpAmb>{tpAmb}</tpAmb>{docTag}<chNFe>{cleanKey}</chNFe><dhEvento>{dhEvento}</dhEvento><tpEvento>110111</tpEvento><nSeqEvento>{nSeqEvento}</nSeqEvento><verEvento>1.00</verEvento><detEvento versao=""1.00""><descEvento>Cancelamento</descEvento><nProt>{nProt.Trim()}</nProt><xJust>{sanitizedJust}</xJust></detEvento></infEvento></evento></envEvento>";

            var eventoDoc = new XmlDocument { PreserveWhitespace = true };
            eventoDoc.LoadXml(eventoXml);

            var infEventoEl = (XmlElement)eventoDoc.SelectSingleNode("//*[local-name()='infEvento']")!;
            SignElementById(eventoDoc, certificate, infEventoEl, idEvento);

            var endpoints = configStore.GetConfig().ResolveEndpoints(resolvedCuf, tpAmb == 2, requestedProvider);
            if (string.IsNullOrWhiteSpace(endpoints.NFeRecepcaoEvento))
                throw new InvalidOperationException($"Endpoint NFeRecepcaoEvento não configurado para a UF {resolvedCuf}.");

            var soapEnvelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><nfeDadosMsg xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4\">{eventoDoc.DocumentElement!.OuterXml}</nfeDadosMsg></soap12:Body></soap12:Envelope>";

            var soapResponse = await SendSoap12Async(
                certificate,
                endpoints.NFeRecepcaoEvento,
                soapEnvelope,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEvento");

            var resultNode = ExtractSoapResultChild(soapResponse, "nfeResultMsg");
            return resultNode?.OuterXml ?? soapResponse;
        }

        private static string ResolveCufCode(string ufOrCode)
        {
            var trimmed = ufOrCode.Trim();
            if (trimmed.All(char.IsDigit))
                return trimmed.PadLeft(2, '0');

            return UfToCode.TryGetValue(trimmed, out var code) ? code : "52";
        }

        private static (string Chave, string Dv) CalculateAccessKey(
            int ufEmitente,
            DateTime dataEmissao,
            string cnpjCpfEmitente,
            int modelo,
            int serie,
            long numero,
            int tipoEmissao,
            int cNf)
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
              .Append(cNf.ToString("D8"));

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

            parentNode.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
        }

        private static string BuildNfeProcXml(XmlDocument body, XmlElement nfeOriginal, XmlNode? resultadoNode, string versao)
        {
            var retornoDoc = new XmlDocument { PreserveWhitespace = false };
            var rawResultXml = resultadoNode?.OuterXml ?? "<retEnviNFe xmlns=\"http://www.portalfiscal.inf.br/nfe\" />";
            retornoDoc.LoadXml($"<retornoWrapper xmlns=\"{NfeNamespace}\">{rawResultXml}</retornoWrapper>");

            var nsRet = new XmlNamespaceManager(retornoDoc.NameTable);
            nsRet.AddNamespace("n", NfeNamespace);

            var protNFeNode = retornoDoc.SelectSingleNode("//n:protNFe", nsRet);

            var nfeProc = new XmlDocument { PreserveWhitespace = true };
            nfeProc.LoadXml($@"<nfeProc xmlns=""{NfeNamespace}"" versao=""{versao}"" />");
            var root = nfeProc.DocumentElement!;

            root.AppendChild(nfeProc.ImportNode(nfeOriginal, true));

            if (protNFeNode != null)
            {
                root.AppendChild(nfeProc.ImportNode(protNFeNode, true));
            }
            else
            {
                var protNFe = nfeProc.CreateElement("protNFe", NfeNamespace);
                protNFe.SetAttribute("versao", versao);

                var infProt = nfeProc.CreateElement("infProt", NfeNamespace);
                foreach (var field in new[] { "tpAmb", "verAplic", "cStat", "xMotivo", "cUF", "chNFe", "dhRecbto", "nProt", "digVal" })
                {
                    var found = retornoDoc.SelectSingleNode($"//n:{field}", nsRet);
                    if (found != null)
                    {
                        var el = nfeProc.CreateElement(field, NfeNamespace);
                        el.InnerText = found.InnerText;
                        infProt.AppendChild(el);
                    }
                }

                protNFe.AppendChild(infProt);
                root.AppendChild(protNFe);
            }

            return nfeProc.OuterXml;
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

