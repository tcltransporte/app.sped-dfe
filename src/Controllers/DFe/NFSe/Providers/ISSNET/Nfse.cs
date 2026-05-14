using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Globalization;
using System.Linq;
using System.Net.Security; // Necessário para SslPolicyErrors (HttpClientHandler)


namespace application.services.Controllers.DFe.NFSe.Providers.ISSNET
{

    // Namespace padrão ABRASF v2.04
    public static class NfseConstants
    {

        public const string NfseNamespace = "http://www.abrasf.org.br/nfse.xsd";
        public const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

        public const string SoapActionGerar = "http://nfse.abrasf.org.br/GerarNfse";
        public const string SoapActionConsultar = "http://nfse.abrasf.org.br/ConsultarNfseServicoPrestado";
        public const string SoapActionCancelar = "http://nfse.abrasf.org.br/CancelarNfse";

        public const string Version = "2.04";
    }

    public static class ISSNET
    {

        // --- DADOS DE AUTENTICAÇÃO E ENDPOINT ---
        private const string PFX_FILE_PATH = "C:\\Certificados\\7e0e2508285b1e0a QUALITY.pfx";
        private const string PFX_PASSWORD = "321456qG"; // Senha do certificado Quality
        private const string SOAP_ENDPOINT = "https://nfse.issnetonline.com.br/abrasf204/goiania/nfse.asmx";

        // --- DADOS DO PRESTADOR ATUAL (Quality Instalacoes Industria e Comercio Ltda) ---
        private const string PRESTADOR_CNPJ = "31038005000137";
        private const string PRESTADOR_IM = "4659821";
        private const string CODIGO_MUNICIPIO_GOIANIA = "5208707";

        // --- DADOS DE SEQUÊNCIA DO RPS (PARA GERAÇÃO) ---
        // Série VAZIA ("") é o padrão mais seguro quando o portal da prefeitura é o emissor primário.
        private const string RPS_SERIE_TO_USE = "1";
        // Usamos 2 como valor de segurança, se a NFSe de exemplo era a 1.
        private const int RPS_NUMBER_TO_USE = 4;

        // ID do elemento a ser assinado (RPS_ + Número do RPS)
        private static readonly string ELEMENT_TO_SIGN_ID = "RPS_" + RPS_NUMBER_TO_USE.ToString();

        /*
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Iniciando utilitário de NFSe...");

            try
            {
                // 1. Tenta GERAR a NFS-e com o número de RPS fixo
                Console.WriteLine($"\n--- Tentando GERAR NFSe com RPS: {RPS_NUMBER_TO_USE} e Série: '{RPS_SERIE_TO_USE}' ---");

                //await ConsultarUltimoRpsAsync();

                await CancelarNfseAsync(1, "ID_CANC", "1");

                await GerarNfseAsync(ELEMENT_TO_SIGN_ID, RPS_NUMBER_TO_USE);

                // --- CÓDIGO DE CONSULTA COMENTADO PARA EVITAR O ERRO S:CLIENT NA CONSULTA ---
                // int proximoRps = await ConsultarUltimoRpsAsync();
                // Console.WriteLine($"\n[INFO] A consulta retornou o próximo RPS esperado: {proximoRps}");
                // --- FIM DO CÓDIGO DE CONSULTA COMENTADO ---

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERRO FATAL: {ex.Message}");
                Console.ResetColor();
            }
        }
        */

        // O método ConsultarUltimoRpsAsync foi mantido, mas não é chamado pelo Main.
        /// <summary>
        /// Consulta as últimas NFS-e emitidas para determinar o próximo número de RPS.
        /// </summary>
        private static async Task<int> ConsultarUltimoRpsAsync()
        {

            Console.WriteLine("\n[CONSULTA] Tentando determinar a última numeração de RPS utilizada...");

            // 1. Cria o XML de Consulta
            string consultaXmlContent = $@"
<ConsultarNfseServicoPrestadoEnvio xmlns=""{NfseConstants.NfseNamespace}"">
    <Prestador>
        <CpfCnpj>
            <Cnpj>{PRESTADOR_CNPJ}</Cnpj>
        </CpfCnpj>
        <InscricaoMunicipal>{PRESTADOR_IM}</InscricaoMunicipal>
    </Prestador>
    <PeriodoEmissao>
        <DataInicial>2024-01-01</DataInicial>
        <DataFinal>{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</DataFinal>
    </PeriodoEmissao>
    <Pagina>1</Pagina>
</ConsultarNfseServicoPrestadoEnvio>";

            /*
            // 2. Constrói o Envelope SOAP para a Consulta
            string cabecalho = $"<cabecalho versao=\"{NfseConstants.Version}\" xmlns=\"{NfseConstants.NfseNamespace}\"><versaoDados>{NfseConstants.Version}</versaoDados></cabecalho>";

            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
    <soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
      <soap:Body>
        <ConsultarNfseServicoPrestado xmlns=""http://nfse.abrasf.org.br"">
          <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
          <nfseDadosMsg>{consultaXmlContent}</nfseDadosMsg>
        </ConsultarNfseServicoPrestado>
      </soap:Body>
    </soap:Envelope>";
            */

            // Cria o XML de Cabeçalho (nfseCabecMsg)
            string cabecalho = $"<cabecalho versao=\"{NfseConstants.Version}\" xmlns=\"{NfseConstants.NfseNamespace}\"><versaoDados>{NfseConstants.Version}</versaoDados></cabecalho>";

            // Cria o XML de Dados (nfseDadosMsg) com o nó GerarNfseEnvio.
            //string dadosMsg = $"<GerarNfseEnvio xmlns=\"{NfseConstants.NfseNamespace}\"><Rps>{signedRpsInnerContent}</Rps></GerarNfseEnvio>";

            // Constrói o Envelope SOAP
            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <ConsultarNfseServicoPrestado xmlns=""http://nfse.abrasf.org.br"">
      <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
      <nfseDadosMsg>{consultaXmlContent}</nfseDadosMsg>
    </ConsultarNfseServicoPrestado>
  </soap:Body>
</soap:Envelope>";

            // 3. Envia a requisição de Consulta
            string responseXml = await SendSoapRequestAsync(SOAP_ENDPOINT, soapEnvelope, NfseConstants.SoapActionConsultar);

            Console.WriteLine(responseXml);

            // 4. Processa a resposta
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(responseXml);

            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("s", "http://schemas.xmlsoap.org/soap/envelope/");
            nsmgr.AddNamespace("ws", "http://nfse.abrasf.org.br");
            nsmgr.AddNamespace("nfse", NfseConstants.NfseNamespace);

            // Verifica erros de negócio na resposta da consulta
            XmlNode erroNode = doc.SelectSingleNode("//nfse:MensagemRetorno", nsmgr);
            if (erroNode != null)
            {
                string codigo = erroNode.SelectSingleNode("nfse:Codigo", nsmgr)?.InnerText;
                string mensagem = erroNode.SelectSingleNode("nfse:Mensagem", nsmgr)?.InnerText;
                Console.WriteLine($"[ERRO NA CONSULTA] Código: {codigo}, Mensagem: {mensagem}. Retornando RPS padrão ({RPS_NUMBER_TO_USE}).");
                return RPS_NUMBER_TO_USE;
            }

            // Tenta encontrar o número da última NFS-e/RPS
            XmlNodeList nfseNodes = doc.SelectNodes("//nfse:Nfse/nfse:InfNfse/nfse:Numero", nsmgr);

            if (nfseNodes != null && nfseNodes.Count > 0)
            {
                // Pega todos os números de NFSe encontrados e calcula o máximo
                int maxNfseNumber = nfseNodes.Cast<XmlNode>()
                                              .Select(n => int.Parse(n.InnerText))
                                              .Max();

                // O próximo RPS deve ser o número da última NFSe + 1
                return maxNfseNumber + 1;
            }

            // Se não houver notas emitidas, o próximo RPS é 1.
            return 1;
        }

        //-------------------------------------------------------------------------------------
        //                                  MÉTODO CANCELAMENTO
        //-------------------------------------------------------------------------------------

        /// <summary>
        /// Tenta cancelar uma NFS-e com os dados fornecidos.
        /// </summary>
        private static async Task CancelarNfseAsync(int nfseNumber, string idToSign, string codigoCancelamento)
        {
            Console.WriteLine($"\n[CANCELAMENTO] Tentando cancelar NFSe de número: {nfseNumber}...");

            // 1. Carrega o Certificado Digital
            X509Certificate2 certificate = LoadCertificate(PFX_FILE_PATH, PFX_PASSWORD);

            // 2. Cria o XML de Pedido de Cancelamento (InfPedidoCancelamento) - Não Assinado
            string xmlUnsignedCancelamentoContent = CreateUnsignedCancelamentoXmlContent(nfseNumber, idToSign, codigoCancelamento);

            // 3. Assina o XML. O método retornará o XML completo <CancelarNfseEnvio> com a assinatura.
            string xmlSignedCancelamentoOuterContent = SignXmlCancelamento(xmlUnsignedCancelamentoContent, certificate, idToSign);

            // 4. Cria o Envelope SOAP para o Cancelamento
            string soapEnvelope = CreateSoapEnvelopeCancelamento(xmlSignedCancelamentoOuterContent);

            Console.WriteLine("\n--- CONTEÚDO XML CANCELAMENTO PARA ENVIO (ASSINADO) ---");
            Console.WriteLine(xmlSignedCancelamentoOuterContent);
            Console.WriteLine("--------------------------");

            // 5. Envia a Requisição SOAP
            string response = await SendSoapRequestAsync(SOAP_ENDPOINT, soapEnvelope, NfseConstants.SoapActionCancelar);

            // 6. Imprime a Resposta
            Console.WriteLine("\n--- RESPOSTA DO WEBSERVICE DE CANCELAMENTO ---");
            Console.WriteLine(response);
            Console.WriteLine("---------------------------------------------");
        }

        /// <summary>
        /// Cria o conteúdo do pedido de cancelamento, retornando APENAS o nó <InfPedidoCancelamento> não assinado.
        /// </summary>
        private static string CreateUnsignedCancelamentoXmlContent(int nfseNumber, string id, string codigoCancelamento)
        {
            // Retorna APENAS o conteúdo de InfPedidoCancelamento
            var sb = new StringBuilder();
            sb.Append($"<InfPedidoCancelamento Id=\"{id}\">");
            sb.AppendLine("<IdentificacaoNfse>");
            sb.AppendLine($"<Numero>{nfseNumber}</Numero>");
            sb.AppendLine("<CpfCnpj>");
            sb.AppendLine($"<Cnpj>{PRESTADOR_CNPJ}</Cnpj>");
            sb.AppendLine("</CpfCnpj>");
            sb.AppendLine($"<InscricaoMunicipal>{PRESTADOR_IM}</InscricaoMunicipal>");
            sb.AppendLine($"<CodigoMunicipio>{CODIGO_MUNICIPIO_GOIANIA}</CodigoMunicipio>");
            sb.AppendLine("</IdentificacaoNfse>");
            sb.AppendLine($"<CodigoCancelamento>{codigoCancelamento}</CodigoCancelamento>");
            sb.Append("</InfPedidoCancelamento>");
            return sb.ToString();
        }

        /// <summary>
        /// Assina digitalmente o conteúdo de InfPedidoCancelamento, envolvendo-o em <CancelarNfseEnvio>.
        /// Retorna o XML completo <CancelarNfseEnvio> assinado.
        /// </summary>
        private static string SignXmlCancelamento(string xmlUnsignedCancelamentoContent, X509Certificate2 certificate, string idToSign)
        {
            // 1. Cria a tag pai temporária: CancelarNfseEnvio -> Pedido
            string xmlToSign = $"<CancelarNfseEnvio><Pedido>{xmlUnsignedCancelamentoContent}</Pedido></CancelarNfseEnvio>";
            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.LoadXml(xmlToSign);

            // 2. Encontra o elemento a ser assinado (InfPedidoCancelamento)
            XmlNode declarationNode = doc.SelectSingleNode($"//InfPedidoCancelamento[@Id='{idToSign}']");
            if (declarationNode == null)
            {
                throw new Exception($"Nó com Id='{idToSign}' não encontrado para assinatura do cancelamento.");
            }

            // 3. Cria o objeto SignedXml e define a chave
            SignedXml signedXml = new SignedXml(declarationNode.OwnerDocument);
            signedXml.SigningKey = certificate.GetRSAPrivateKey();

            // 4. Define os métodos de Canonicalização, Assinatura e Digest (padrão NFSe)
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            // 5. Cria a Referência (Reference)
            Reference reference = new Reference($"#{idToSign}");
            reference.DigestMethod = SignedXml.XmlDsigSHA1Url;

            // 6. Adiciona as transformações (Transforms)
            reference.AddTransform(new XmlDsigExcC14NTransform());

            // 7. Adiciona a referência ao objeto SignedXml
            signedXml.AddReference(reference);

            // 8. Adiciona a KeyInfo (informações do certificado)
            signedXml.KeyInfo = new KeyInfo();
            KeyInfoX509Data keyInfoData = new KeyInfoX509Data(certificate);
            signedXml.KeyInfo.AddClause(keyInfoData);

            // 9. Executa a assinatura
            signedXml.ComputeSignature();

            // 10. Obtém o elemento <Signature>
            XmlElement signatureElement = signedXml.GetXml();

            // 11. Insere o <Signature> dentro de <Pedido>
            XmlNode pedidoNode = doc.SelectSingleNode("/CancelarNfseEnvio/Pedido");
            pedidoNode.AppendChild(doc.ImportNode(signatureElement, true));

            // 12. Retorna o conteúdo COMPLETO de <CancelarNfseEnvio>
            return doc.SelectSingleNode("/CancelarNfseEnvio").OuterXml;
        }

        /// <summary>
        /// Cria o envelope SOAP para o método CancelarNfseRequest.
        /// </summary>
        private static string CreateSoapEnvelopeCancelamento(string signedCancelamentoOuterContent)
        {
            // Cria o XML de Cabeçalho (nfseCabecMsg)
            string cabecalho = $"<cabecalho versao=\"{NfseConstants.Version}\" xmlns=\"{NfseConstants.NfseNamespace}\"><versaoDados>{NfseConstants.Version}</versaoDados></cabecalho>";

            // Adiciona a namespace ABRASF ao CancelarNfseEnvio, que o método de assinatura não faz automaticamente.
            string dadosMsgWithNamespace = signedCancelamentoOuterContent.Replace("<CancelarNfseEnvio>", $"<CancelarNfseEnvio xmlns=\"{NfseConstants.NfseNamespace}\">");

            // Constrói o Envelope SOAP usando CancelarNfseRequest, conforme o WSDL
            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <CancelarNfse xmlns=""http://nfse.abrasf.org.br"">
      <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
      <nfseDadosMsg>{dadosMsgWithNamespace}</nfseDadosMsg>
    </CancelarNfse>
  </soap:Body>
</soap:Envelope>";

            return soapEnvelope;
        }


        /// <summary>
        /// Tenta gerar a NFS-e com o número de RPS fornecido.
        /// </summary>
        public static async Task<string> GerarNfseAsync(X509Certificate2 certificate, Schemas.DPS dps)
        {

            var idToSign = "RPS_" + dps?.Informacoes?.NumeroDps;


            Console.WriteLine("\n[GERAÇÃO] Tentando gerar NFSe...");

            // 1. Carrega o Certificado Digital
            //X509Certificate2 certificate = LoadCertificate(PFX_FILE_PATH, PFX_PASSWORD);
            Console.WriteLine($"Certificado carregado: {certificate.Subject}");

            // 2. Cria o XML da Declaração de Serviço (InfDeclaracaoPrestacaoServico) - Não Assinado
            string xmlUnsignedRpsContent = CreateUnsignedRpsXmlContent(idToSign, dps);

            // 3. Assina o XML
            string xmlSignedRpsInnerContent = SignXmlRps(xmlUnsignedRpsContent, certificate, idToSign);

            // 4. Cria o Envelope SOAP
            string soapEnvelope = CreateSoapEnvelope(xmlSignedRpsInnerContent);

            Console.WriteLine("\n--- CONTEÚDO RPS PARA ENVIO (ASSINADO) ---");
            Console.WriteLine(xmlSignedRpsInnerContent);
            Console.WriteLine("--------------------------");

            // 5. Envia a Requisição SOAP
            string response = await SendSoapRequestAsync(SOAP_ENDPOINT, soapEnvelope, NfseConstants.SoapActionGerar);

            // 6. Imprime a Resposta
            Console.WriteLine("\n--- RESPOSTA DO WEBSERVICE ---");
            Console.WriteLine(response);
            Console.WriteLine("------------------------------");

            return response;


        }

        /// <summary>
        /// Carrega o Certificado Digital PFX (A1 ou A3) a partir do caminho do arquivo.
        /// </summary>
        private static X509Certificate2 LoadCertificate(string pfxFilePath, string password)
        {
            if (!File.Exists(pfxFilePath))
            {
                throw new FileNotFoundException($"Arquivo PFX não encontrado: {pfxFilePath}. Verifique o caminho.", pfxFilePath);
            }

            // Carrega o certificado do arquivo PFX, permitindo a exportação e persistência da chave.
            return new X509Certificate2(pfxFilePath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        /// <summary>
        /// Cria o conteúdo do RPS, retornando APENAS o nó <InfDeclaracaoPrestacaoServico> não assinado.
        /// Agora recebe o número do RPS como parâmetro.
        /// </summary>
        private static string CreateUnsignedRpsXmlContent(string id, Schemas.DPS? dps)
        {

            var culture = CultureInfo.InvariantCulture;

            decimal valorZero = 0.00m;


            var sb = new StringBuilder();
            sb.Append($"<InfDeclaracaoPrestacaoServico Id=\"{id}\">");

            // --- Estrutura RPS (Obrigatória para este webservice) ---
            sb.AppendLine("<Rps>");
            sb.AppendLine("<IdentificacaoRps>");
            sb.AppendLine($"<Numero>{dps?.Informacoes?.NumeroDps}</Numero>"); // Número dinâmico (2)
            sb.AppendLine($"<Serie>{dps?.Informacoes?.Serie}</Serie>"); // Série vazia ("")
            sb.AppendLine("<Tipo>1</Tipo>"); // 1 - RPS
            sb.AppendLine("</IdentificacaoRps>");
            // Usando apenas a data (yyyy-MM-dd) para o campo <DataEmissao> do RPS (tipo xsd:date)
            sb.AppendLine($"<DataEmissao>{DateTime.Now.ToString("yyyy-MM-dd HH:mm", culture)}</DataEmissao>");
            sb.AppendLine("<Status>1</Status>"); // 1 - Normal
            sb.AppendLine("</Rps>");

            sb.AppendLine($"<Competencia>{dps?.Informacoes?.Competencia?.ToString("yyyy-MM-dd", culture)}</Competencia>"); // Data de competência do XML de exemplo
            sb.AppendLine("<Servico>");
            sb.AppendLine("<Valores>");
            sb.AppendLine($"<ValorServicos>{dps?.Informacoes?.Valores?.ValoresServico?.Valor?.ToString("F2", culture)}</ValorServicos>");
            sb.AppendLine($"<ValorDeducoes>{valorZero.ToString("F2", culture)}</ValorDeducoes>");
            sb.AppendLine($"<ValorPis>{valorZero.ToString("F2", culture)}</ValorPis>");
            sb.AppendLine($"<ValorCofins>{valorZero.ToString("F2", culture)}</ValorCofins>");
            sb.AppendLine($"<ValorInss>{valorZero.ToString("F2", culture)}</ValorInss>");
            sb.AppendLine($"<ValorIr>{valorZero.ToString("F2", culture)}</ValorIr>");
            sb.AppendLine($"<ValorCsll>{valorZero.ToString("F2", culture)}</ValorCsll>");
            sb.AppendLine($"<OutrasRetencoes>{valorZero.ToString("F2", culture)}</OutrasRetencoes>");
            sb.AppendLine($"<ValorIss>{dps?.Informacoes?.Valores?.Tributos?.Municipal?.Valor?.ToString("F2", culture)}</ValorIss>");
            sb.AppendLine($"<Aliquota>{dps?.Informacoes?.Valores?.Tributos?.Municipal?.Aliquota?.ToString("F2", culture)}</Aliquota>");
            sb.AppendLine($"<DescontoIncondicionado>{valorZero.ToString("F2", culture)}</DescontoIncondicionado>");
            sb.AppendLine($"<DescontoCondicionado>{valorZero.ToString("F2", culture)}</DescontoCondicionado>");
            sb.AppendLine("</Valores>");
            sb.AppendLine("<IssRetido>2</IssRetido>"); // 2 - Não Retido (conforme exemplo)
            sb.AppendLine("<ItemListaServico>14.06</ItemListaServico>");
            sb.AppendLine("<CodigoCnae>3329501</CodigoCnae>");
            sb.AppendLine($"<CodigoTributacaoMunicipio>{dps?.Informacoes?.Servico?.Informacoes?.Descricao}</CodigoTributacaoMunicipio>");
            sb.AppendLine($"<Discriminacao>{dps?.Informacoes?.Servico?.Informacoes?.Descricao}</Discriminacao>");
            sb.AppendLine($"<CodigoMunicipio>{dps?.Informacoes?.Servico?.Localidade?.CodMunicipioPrestacao}</CodigoMunicipio>");
            sb.AppendLine("<ExigibilidadeISS>1</ExigibilidadeISS>"); // 1 - Exigível
            sb.AppendLine($"<MunicipioIncidencia>{dps?.Informacoes?.Servico?.Localidade?.CodMunicipioPrestacao}</MunicipioIncidencia>");
            sb.AppendLine("</Servico>");

            // --- DADOS DO PRESTADOR ---
            sb.AppendLine("<Prestador>");
            sb.AppendLine("<CpfCnpj>");
            sb.AppendLine($"<Cnpj>{dps?.Informacoes?.Prestador?.CNPJ}</Cnpj>");
            sb.AppendLine("</CpfCnpj>");
            sb.AppendLine($"<InscricaoMunicipal>{dps?.Informacoes?.Prestador?.InscricaoMunicipal}</InscricaoMunicipal>");
            sb.AppendLine("</Prestador>");

            // --- DADOS DO TOMADOR ---
            sb.AppendLine("<TomadorServico>");
            sb.AppendLine("<IdentificacaoTomador>");
            sb.AppendLine("<CpfCnpj>");
            sb.AppendLine($"<Cnpj>{dps?.Informacoes?.Tomador?.CNPJ}</Cnpj>");
            sb.AppendLine("</CpfCnpj>");
            sb.AppendLine($"<InscricaoMunicipal>{dps?.Informacoes?.Tomador?.InscricaoMunicipal}</InscricaoMunicipal>");
            sb.AppendLine("</IdentificacaoTomador>");
            sb.AppendLine($"<RazaoSocial>{dps?.Informacoes?.Tomador?.Nome}</RazaoSocial>");
            sb.AppendLine("<Endereco>");
            sb.AppendLine($"<Endereco>{dps?.Informacoes?.Tomador?.Endereco?.Logradouro}</Endereco>");
            sb.AppendLine($"<Numero>{dps?.Informacoes?.Tomador?.Endereco?.Numero}</Numero>");
            sb.AppendLine($"<Complemento>{dps?.Informacoes?.Tomador?.Endereco?.Complemento}</Complemento>");
            sb.AppendLine($"<Bairro>{dps?.Informacoes?.Tomador?.Endereco?.Bairro}</Bairro>");
            sb.AppendLine($"<CodigoMunicipio>{dps?.Informacoes?.Tomador?.Endereco?.Municipio?.CodMunicipio}</CodigoMunicipio>");
            sb.AppendLine($"<Uf>{dps?.Informacoes?.Tomador?.Endereco?.Municipio?.UF}</Uf>");
            sb.AppendLine($"<Cep>{dps?.Informacoes?.Tomador?.Endereco?.Municipio?.CEP}</Cep>");
            sb.AppendLine("</Endereco>");
            //sb.AppendLine("<Contato>");
            //sb.AppendLine("<Telefone>6232691100</Telefone>");
            //sb.AppendLine("</Contato>");
            sb.AppendLine("</TomadorServico>");

            sb.AppendLine("<OptanteSimplesNacional>1</OptanteSimplesNacional>"); // 1 - Sim (conforme exemplo)
            sb.AppendLine("<IncentivoFiscal>2</IncentivoFiscal>"); // 2 - Não (conforme exemplo)
            sb.Append("</InfDeclaracaoPrestacaoServico>");

            return sb.ToString();

        }

        /// <summary>
        /// Assina digitalmente o conteúdo do RPS. 
        /// </summary>
        private static string SignXmlRps(string xmlUnsignedRpsContent, X509Certificate2 certificate, string idToSign)
        {
            // 1. Cria a tag pai temporária para que o XML possa ser carregado e assinado corretamente.
            string xmlToSign = $"<DeclaracaoPrestacaoServico>{xmlUnsignedRpsContent}</DeclaracaoPrestacaoServico>";
            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.LoadXml(xmlToSign);

            // 2. Encontra o elemento a ser assinado (InfDeclaracaoPrestacaoServico)
            XmlNode declarationNode = doc.SelectSingleNode($"/*/*[@Id='{idToSign}']");
            if (declarationNode == null)
            {
                throw new Exception($"Nó com Id='{idToSign}' não encontrado para assinatura.");
            }

            // 3. Cria o objeto SignedXml
            SignedXml signedXml = new SignedXml(declarationNode.OwnerDocument);
            signedXml.SigningKey = certificate.GetRSAPrivateKey();

            // 4. Define os métodos de Canonicalização, Assinatura e Digest (conforme ABRASF)
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl; // C14N Exclusivo
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url; // RSA-SHA1 (padrão NFSe)

            // 5. Cria a Referência (Reference)
            Reference reference = new Reference($"#{idToSign}");
            reference.DigestMethod = SignedXml.XmlDsigSHA1Url; // SHA1 (padrão NFSe)

            // 6. Adiciona as transformações (Transforms)
            reference.AddTransform(new XmlDsigExcC14NTransform());

            // 7. Adiciona a referência ao objeto SignedXml
            signedXml.AddReference(reference);

            // 8. Adiciona a KeyInfo (informações do certificado)
            signedXml.KeyInfo = new KeyInfo();
            KeyInfoX509Data keyInfoData = new KeyInfoX509Data(certificate);
            signedXml.KeyInfo.AddClause(keyInfoData);

            // 9. Executa a assinatura
            signedXml.ComputeSignature();

            // 10. Obtém o elemento <Signature>
            XmlElement signatureElement = signedXml.GetXml();

            // 11. Insere o <Signature> no local correto (dentro de <DeclaracaoPrestacaoServico> e após <InfDeclaracaoPrestacaoServico>)
            XmlNode declarationPrestacaoServicoNode = doc.SelectSingleNode($"/*");
            declarationPrestacaoServicoNode.AppendChild(doc.ImportNode(signatureElement, true));

            // 12. Retorna o conteúdo INTERNO de <DeclaracaoPrestacaoServico>, que é a estrutura esperada pelo <Rps>
            return declarationPrestacaoServicoNode.InnerXml;
        }

        /// <summary>
        /// Cria o envelope SOAP para o método GerarNfse.
        /// </summary>
        private static string CreateSoapEnvelope(string signedRpsInnerContent)
        {
            // Cria o XML de Cabeçalho (nfseCabecMsg)
            string cabecalho = $"<cabecalho versao=\"{NfseConstants.Version}\" xmlns=\"{NfseConstants.NfseNamespace}\"><versaoDados>{NfseConstants.Version}</versaoDados></cabecalho>";

            // Cria o XML de Dados (nfseDadosMsg) com o nó GerarNfseEnvio.
            string dadosMsg = $"<GerarNfseEnvio xmlns=\"{NfseConstants.NfseNamespace}\"><Rps>{signedRpsInnerContent}</Rps></GerarNfseEnvio>";

            // Constrói o Envelope SOAP
            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <GerarNfse xmlns=""http://nfse.abrasf.org.br"">
      <nfseCabecMsg>{cabecalho}</nfseCabecMsg>
      <nfseDadosMsg>{dadosMsg}</nfseDadosMsg>
    </GerarNfse>
  </soap:Body>
</soap:Envelope>";

            return soapEnvelope;
        }

        /// <summary>
        /// Envia a requisição SOAP via HttpClient.
        /// </summary>
        private static async Task<string> SendSoapRequestAsync(string endpoint, string soapEnvelope, string soapAction)
        {
            // 1. Carrega o Certificado Digital novamente para o cliente SSL/TLS
            X509Certificate2 clientCertificate = LoadCertificate(PFX_FILE_PATH, PFX_PASSWORD);

            // 2. Configura o Handler para usar o certificado como Cliente SSL/TLS
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(clientCertificate);

            // Necessário para ignorar possíveis erros de CA não confiável no ambiente de teste/produção
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

            using (var client = new HttpClient(handler))
            {
                var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

                // O SOAPAction é adicionado ao header
                client.DefaultRequestHeaders.Add("SOAPAction", soapAction);

                Console.WriteLine($"\nEnviando requisição para: {endpoint} com SOAPAction: {soapAction}...");

                var response = await client.PostAsync(endpoint, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                // Retorna o conteúdo mesmo em caso de erro 500, para que o s:Fault possa ser lido.
                return responseContent;
            }
        }
    }

}
