// O namespace 'NfseGynWs' é o nome que você deu à sua Referência de Serviço.
using ServiceReference1;
using ServiceReference1;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.Text;
using System.Xml.Linq; // Para manipulação de XML, se necessário

namespace NfseConsultaConsole
{
    class Program
    {
        // Função utilitária para codificar o XML em uma string segura para o Web Service
        static string EncodeXml(string xmlContent)
        {
            // O padrão ISSNet/ABRASF geralmente requer que < seja substituído por &lt; e > por &gt;
            return xmlContent.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static async Task Main(string[] args)
        {
            try
            {
                // 1. Definição do XML de Cabeçalho (Cabecalho)
                string cabecalhoXml = @"
                    <cabecalho versao=""2.04"" xmlns=""http://www.abrasf.org.br/nfse.xsd"">
                        <versaoDados>2.04</versaoDados>
                    </cabecalho>";

                string nfseCabecMsg = EncodeXml(cabecalhoXml.Trim());

                // 2. Definição do XML de Dados (ConsultarDadosCadastraisEnvio)
                // Substitua com os dados reais do prestador
                string dadosEnvioXml = @"
                    <ConsultarDadosCadastraisEnvio xmlns=""http://www.abrasf.org.br/nfse.xsd"">
                        <Prestador>
                            <CpfCnpj>
                                <Cnpj>31038005000137</Cnpj> 
                            </CpfCnpj>
                            <InscricaoMunicipal>4659821</InscricaoMunicipal>
                        </Prestador>
                    </ConsultarDadosCadastraisEnvio>";

                // O Web Service da ISSNet pode exigir que a assinatura digital esteja na tag de dados
                // Se a assinatura for necessária para este serviço, ela deve ser incluída aqui.

                string nfseDadosMsg = EncodeXml(dadosEnvioXml.Trim());


                // 1. Defina o Endereço do Serviço (EndpointAddress)
                // O WSDL aponta para o endereço genérico http://ws.pbh.gov.br,
                // mas você deve usar o endereço real para Goiânia:
                string serviceUrl = "https://nfse.issnetonline.com.br/abrasf204/goiania/nfse.asmx";
                EndpointAddress endpointAddress = new EndpointAddress(serviceUrl);

                // 2. Crie a Configuração do Binding
                // BasicHttpBinding é o binding padrão para SOAP 1.1 (o que o WSDL indica).
                BasicHttpBinding binding = new BasicHttpBinding();

                // Configurações de segurança: O endpoint usa HTTPS, então o Security Mode deve ser Transport.
                if (serviceUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                {
                    binding.Security.Mode = BasicHttpSecurityMode.Transport;
                }

                // O endereço SOAP está definido no WSDL como http://ws.pbh.gov.br
                // Use o endereço real para Goiânia: https://nfse.issnetonline.com.br/abrasf204/goiania/nfse.asmx 
                // A classe cliente gerada pode ter um nome como 'nfse' ou 'NfseWSService'.
                var client = new nfseClient(binding, endpointAddress); // Nome da classe cliente pode variar (ex: NfseWSService, nfse)

                var cert = new X509Certificate2("C:\\Certificados\\7e0e2508285b1e0a QUALITY.pfx", "321456qG", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                client.ClientCredentials.ClientCertificate.Certificate = cert;

                // O método real exposto no WSDL da ISSNet/Goiânia será:
                // ConsultarDadosCadastrais(string nfseCabecMsg, string nfseDadosMsg)

                Console.WriteLine("Enviando requisição ConsultarDadosCadastrais...");

                // A resposta será uma string XML codificada em HTML
                var respostaXmlCodificada = await client.ConsultarDadosCadastraisAsync(nfseCabecMsg, nfseDadosMsg);

                // Descodificar a resposta para facilitar a leitura
                //string respostaXmlDecodificada = WebUtility.HtmlDecode(respostaXmlCodificada);

                Console.WriteLine("\n--- Resposta do Web Service ---");
                Console.WriteLine(respostaXmlCodificada);
                Console.WriteLine("------------------------------");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocorreu um erro: {ex.Message}");
                // Adicione detalhes do InnerException para debug, se necessário
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Detalhes: {ex.InnerException.Message}");
                }
            }
            Console.WriteLine("\nPressione qualquer tecla para sair.");
            Console.ReadKey();
        }
    }
}