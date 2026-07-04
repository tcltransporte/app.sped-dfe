using application.services.Controllers.DFe.NFSe.Providers;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace application.services.Controllers.DFe.NFSe.Providers.ISSNET
{
    public static class NfseConstants
    {
        public const string NfseNamespace = "http://www.abrasf.org.br/nfse.xsd";
        public const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        public const string SoapNamespace = "http://nfse.abrasf.org.br";
        public const string SoapActionGerar = "http://nfse.abrasf.org.br/GerarNfse";
        public const string Version = "2.04";
    }

    public sealed class IssNetGoianiaProvider : INFSeProvider
    {
        private const string CodigoMunicipioGoiania = "5208707";
        private const string Endpoint = "https://nfse.issnetonline.com.br/abrasf204/goiania/nfse.asmx";

        public string Name => "ISS.NET Goiânia";

        public bool Supports(NFSeProviderContext context)
        {
            return context.CodigoMunicipio == CodigoMunicipioGoiania ||
                context.MunicipioIncidencia == CodigoMunicipioGoiania ||
                context.MunicipioPrestacao == CodigoMunicipioGoiania;
        }

        public async Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument xml)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (xml.DocumentElement == null) throw new InvalidOperationException("XML de NFS-e vazio ou inválido.");

            var gerarNfseEnvio = PrepareGerarNfseEnvio(xml, certificate);
            var soapEnvelope = CreateSoapEnvelope(gerarNfseEnvio.OuterXml);

            return await SendSoapRequestAsync(certificate, Endpoint, soapEnvelope, NfseConstants.SoapActionGerar);
        }

        private static XmlElement PrepareGerarNfseEnvio(XmlDocument source, X509Certificate2 certificate)
        {
            var doc = Clone(source);
            var root = doc.DocumentElement!;

            if (root.LocalName == "GerarNfseEnvio")
            {
                EnsureAbrasfNamespace(root);
                SignRpsIfNeeded(doc, certificate);
                return root;
            }

            var wrapper = new XmlDocument { PreserveWhitespace = true };
            var gerar = wrapper.CreateElement("GerarNfseEnvio", NfseConstants.NfseNamespace);
            wrapper.AppendChild(gerar);

            if (root.LocalName == "Rps")
            {
                gerar.AppendChild(wrapper.ImportNode(root, true));
            }
            else
            {
                var rps = wrapper.CreateElement("Rps", NfseConstants.NfseNamespace);
                gerar.AppendChild(rps);

                if (root.LocalName == "InfDeclaracaoPrestacaoServico")
                {
                    var declaracao = wrapper.CreateElement("DeclaracaoPrestacaoServico", NfseConstants.NfseNamespace);
                    declaracao.AppendChild(wrapper.ImportNode(root, true));
                    rps.AppendChild(declaracao);
                }
                else
                {
                    rps.AppendChild(wrapper.ImportNode(root, true));
                }
            }

            SignRpsIfNeeded(wrapper, certificate);
            return wrapper.DocumentElement!;
        }

        private static XmlDocument Clone(XmlDocument source)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(source.OuterXml);
            return doc;
        }

        private static void EnsureAbrasfNamespace(XmlElement root)
        {
            if (string.IsNullOrWhiteSpace(root.NamespaceURI))
                root.SetAttribute("xmlns", NfseConstants.NfseNamespace);
        }

        private static void SignRpsIfNeeded(XmlDocument doc, X509Certificate2 certificate)
        {
            if (doc.SelectSingleNode("//*[local-name()='DeclaracaoPrestacaoServico']/*[local-name()='Signature']") != null)
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

        private static string CreateSoapEnvelope(string gerarNfseEnvio)
        {
            var cabecalho = $"<cabecalho versao=\"{NfseConstants.Version}\" xmlns=\"{NfseConstants.NfseNamespace}\"><versaoDados>{NfseConstants.Version}</versaoDados></cabecalho>";

            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <GerarNfse xmlns=""{NfseConstants.SoapNamespace}"">
      <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
      <nfseDadosMsg>{gerarNfseEnvio}</nfseDadosMsg>
    </GerarNfse>
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
