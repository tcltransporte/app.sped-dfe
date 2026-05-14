/********************************************************************************/
/* Projeto: Biblioteca ZeusNFe                                                  */
/* Biblioteca C# para emissão de Nota Fiscal Eletrônica - NFe e Nota Fiscal de  */
/* Consumidor Eletrônica - NFC-e (http://www.nfe.fazenda.gov.br)                */
/*                                                                              */
/* Direitos Autorais Reservados (c) 2014 Adenilton Batista da Silva             */
/*                                       Zeusdev Tecnologia LTDA ME             */
/*                                                                              */
/*  Você pode obter a última versão desse arquivo no GitHub                     */
/* localizado em https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe               */
/*                                                                              */
/*                                                                              */
/*  Esta biblioteca é software livre; você pode redistribuí-la e/ou modificá-la */
/* sob os termos da Licença Pública Geral Menor do GNU conforme publicada pela  */
/* Free Software Foundation; tanto a versão 2.1 da Licença, ou (a seu critério) */
/* qualquer versão posterior.                                                   */
/*                                                                              */
/*  Esta biblioteca é distribuída na expectativa de que seja útil, porém, SEM   */
/* NENHUMA GARANTIA; nem mesmo a garantia implícita de COMERCIABILIDADE OU      */
/* ADEQUAÇÃO A UMA FINALIDADE ESPECÍFICA. Consulte a Licença Pública Geral Menor*/
/* do GNU para mais detalhes. (Arquivo LICENÇA.TXT ou LICENSE.TXT)              */
/*                                                                              */
/*  Você deve ter recebido uma cópia da Licença Pública Geral Menor do GNU junto*/
/* com esta biblioteca; se não, escreva para a Free Software Foundation, Inc.,  */
/* no endereço 59 Temple Street, Suite 330, Boston, MA 02111-1307 USA.          */
/* Você também pode obter uma copia da licença em:                              */
/* http://www.opensource.org/licenses/lgpl-license.php                          */
/*                                                                              */
/* Zeusdev Tecnologia LTDA ME - adenilton@zeusautomacao.com.br                  */
/* http://www.zeusautomacao.com.br/                                             */
/* Rua Comendador Francisco josé da Cunha, 111 - Itabaiana - SE - 49500-000     */
/********************************************************************************/

using CTe.Classes;
using CTe.Classes.Informacoes;
using CTe.Classes.Informacoes.infCTeNormal.infModals;
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Recepcao;
using CTe.Classes.Servicos.Tipos;
using CTe.Utils.Validacao;
using DFe.Classes.Entidades;
using DFe.Utils;
using DFe.Utils.Assinatura;
using Shared.DFe.Utils;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using CteEletronica = CTe.Classes.CTe;

namespace CTe.Utils.CTe
{

    public static class ExtCTe
    {


        /// <summary>
        ///     Carrega um arquivo XML para um objeto da classe CTe
        /// </summary>
        /// <param name="cte"></param>
        /// <param name="arquivoXml">arquivo XML</param>
        /// <returns>Retorna uma NFe carregada com os dados do XML</returns>
        public static CteEletronica CarregarDeArquivoXml(this CteEletronica cte, string arquivoXml)
        {
            var s = FuncoesXml.ObterNodeDeArquivoXml(typeof(CteEletronica).Name, arquivoXml);
            return FuncoesXml.XmlStringParaClasse<CteEletronica>(s);
        }

        /// <summary>
        ///     Converte o objeto CTe para uma string no formato XML
        /// </summary>
        /// <param name="cte"></param>
        /// <returns>Retorna uma string no formato XML com os dados da CTe</returns>
        public static string ObterXmlString(this CteEletronica cte)
        {
            return FuncoesXml.ClasseParaXmlString(cte);
        }

        /// <summary>
        ///     Coverte uma string XML no formato CTe para um objeto CTe
        /// </summary>
        /// <param name="cte"></param>
        /// <param name="xmlString"></param>
        /// <returns>Retorna um objeto do tipo CTe</returns>
        public static CteEletronica CarregarDeXmlString(this CteEletronica cte, string xmlString)
        {
            var s = FuncoesXml.ObterNodeDeStringXml(typeof(CteEletronica).Name, xmlString);
            return FuncoesXml.XmlStringParaClasse<CteEletronica>(s);
        }

        /// <summary>
        ///     Gera id, cdv, assina e faz alguns ajustes nos dados da classe CTe antes de utilizá-la
        /// </summary>
        /// <param name="cte"></param>
        /// <param name="configuracaoServico"></param>
        /// <returns>Retorna um objeto CTe devidamente tradado</returns>
        public static void ValidaSchema(this CteEletronica cte, ConfiguracaoServico configuracaoServico = null)
        {
            if (cte == null) throw new ArgumentNullException("cte");

            var xmlValidacao = cte.ObterXmlString();

            var servicoInstancia = configuracaoServico ?? ConfiguracaoServico.Instancia;

            if (!servicoInstancia.IsValidaSchemas)
                return;

            switch (cte.infCte.versao)
            {
                case versao.ve200:
                    Validador.Valida(xmlValidacao, "cte_v2.00.xsd", servicoInstancia);
                    break;
                case versao.ve300:
                    Validador.Valida(xmlValidacao, "cte_v3.00.xsd", servicoInstancia);
                    break;
                case versao.ve400:
                    Validador.Valida(xmlValidacao, "cte_v4.00.xsd", servicoInstancia);
                    break;
                default:
                    throw new InvalidOperationException("Nos achamos um erro na hora de validar o schema, " +
                                                        "a versão está inválida, somente é permitido " +
                                                        "versão 2.00 é 3.00");
            }

            if (cte.infCte.ide.tpCTe != tpCTe.Anulacao && cte.infCte.ide.tpCTe != tpCTe.Complemento) // Ct-e do Tipo Anulação/Complemento não tem Informações do Modal
            {
                var xmlModal = FuncoesXml.ClasseParaXmlString(cte.infCte.infCTeNorm.infModal);

                switch (cte.infCte.infCTeNorm.infModal.versaoModal)
                {
                    case versaoModal.veM200:
                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aereo))
                        {
                            Validador.Valida(xmlModal, "cteModalAereo_v2.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aquav))
                        {
                            Validador.Valida(xmlModal, "cteModalAquaviario_v2.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(duto))
                        {
                            Validador.Valida(xmlModal, "cteModalDutoviario_v2.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(ferrov))
                        {
                            Validador.Valida(xmlModal, "cteModalFerroviario_v2.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(rodo))
                        {
                            Validador.Valida(xmlModal, "cteModalRodoviario_v2.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(multimodal))
                        {
                            Validador.Valida(xmlModal, "cteMultimodal_v2.00.xsd", servicoInstancia);
                        }
                        break;
                    case versaoModal.veM300:
                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aereo))
                        {
                            Validador.Valida(xmlModal, "cteModalAereo_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aquav))
                        {
                            Validador.Valida(xmlModal, "cteModalAquaviario_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(duto))
                        {
                            Validador.Valida(xmlModal, "cteModalDutoviario_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(ferrov))
                        {
                            Validador.Valida(xmlModal, "cteModalFerroviario_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(rodo))
                        {
                            Validador.Valida(xmlModal, "cteModalRodoviario_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(multimodal))
                        {
                            Validador.Valida(xmlModal, "cteMultimodal_v3.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(rodoOS))
                        {
                            Validador.Valida(xmlModal, "cteModalRodoviarioOS_v3.00.xsd", servicoInstancia);
                        }
                        break;
                    case versaoModal.veM400:
                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aereo))
                        {
                            Validador.Valida(xmlModal, "cteModalAereo_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(aquav))
                        {
                            Validador.Valida(xmlModal, "cteModalAquaviario_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(duto))
                        {
                            Validador.Valida(xmlModal, "cteModalDutoviario_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(ferrov))
                        {
                            Validador.Valida(xmlModal, "cteModalFerroviario_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(rodo))
                        {
                            Validador.Valida(xmlModal, "cteModalRodoviario_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(multimodal))
                        {
                            Validador.Valida(xmlModal, "cteMultimodal_v4.00.xsd", servicoInstancia);
                        }

                        if (cte.infCte.infCTeNorm.infModal.ContainerModal.GetType() == typeof(rodoOS))
                        {
                            Validador.Valida(xmlModal, "cteModalRodoviarioOS_v4.00.xsd", servicoInstancia);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Nos achamos um erro na hora de validar o schema, " +
                                                            "a versão está inválida, somente é permitido " +
                                                            "versão 2.00 é 3.00");
                }
            }
        }

        public static XmlDocument Assina(XmlDocument doc, X509Certificate2 certificate)
        {

            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("ns", "http://www.portalfiscal.inf.br/cte");

            // Localiza os nós principais
            var root = doc.DocumentElement; // Tag <CTe>
            var ide = doc.SelectSingleNode("//ns:ide", ns);
            var emit = doc.SelectSingleNode("//ns:emit", ns);
            var infCte = (XmlElement)doc.SelectSingleNode("//ns:infCte", ns);

            if (infCte == null) throw new Exception("Tag <infCte> não encontrada.");

            // 1. Extração de dados e geração da chave (Conversão de tipos conforme sua necessidade)
            var dadosChave = ChaveFiscal.ObterChave(
                int.Parse(ide.SelectSingleNode("ns:cUF", ns).InnerText),
                DateTime.Parse(ide.SelectSingleNode("ns:dhEmi", ns).InnerText),
                emit.SelectSingleNode("ns:CNPJ", ns).InnerText,
                int.Parse(ide.SelectSingleNode("ns:mod", ns).InnerText),
                int.Parse(ide.SelectSingleNode("ns:serie", ns).InnerText),
                long.Parse(ide.SelectSingleNode("ns:nCT", ns).InnerText),
                int.Parse(ide.SelectSingleNode("ns:tpEmis", ns).InnerText),
                int.Parse(ide.SelectSingleNode("ns:cCT", ns).InnerText)
            );

            // 2. Atualiza os valores de ID e Dígito Verificador no XML
            infCte.SetAttribute("Id", "CTe" + dadosChave.Chave);
            ide.SelectSingleNode("ns:cDV", ns).InnerText = dadosChave.DigitoVerificador.ToString();

            // 3. Chama sua função que gera o XmlElement da assinatura
            // Importante: Passamos o doc para que o Elemento seja criado no mesmo contexto
            // Assina com o XML exatamente como será transmitido, evitando diferenças de digest (cStat 297)
            XmlElement xmlAssinatura = AssinaturaDigital.Assina(doc, infCte.GetAttribute("Id"), certificate, cfgServicoRemoverAcentos: false);

            // 4. Importa e anexa a assinatura ao documento
            // Como o XmlElement vem de uma função, precisamos garantir que ele pertença ao 'doc'
            var importSignature = doc.ImportNode(xmlAssinatura, true);
            root.AppendChild(importSignature);

            return doc;
        }

        /// <summary>
        ///     Assina um objeto CTe
        /// </summary>
        /// <param name="cte"></param>
        /// <param name="configuracaoServico"></param>
        /// <returns>Retorna um objeto do tipo CTe assinado</returns>
        public static void Assina(this CteEletronica cte, X509Certificate2 certificate)
        {
            if (cte == null) throw new ArgumentNullException("cte");

            //var configServico = configuracaoServico ?? ConfiguracaoServico.Instancia;

            var modeloDocumentoFiscal = cte.infCte.ide.mod;
            var tipoEmissao = (int)cte.infCte.ide.tpEmis;
            var codigoNumerico = cte.infCte.ide.cCT;
            var estado = cte.infCte.ide.cUF;
            var dataEHoraEmissao = cte.infCte.ide.dhEmi;
            var cnpj = cte.infCte.emit.CNPJ;
            var numeroDocumento = cte.infCte.ide.nCT;
            int serie = cte.infCte.ide.serie;

            var dadosChave = ChaveFiscal.ObterChave(estado, dataEHoraEmissao, cnpj, modeloDocumentoFiscal, serie, numeroDocumento, tipoEmissao, codigoNumerico);

            cte.infCte.Id = "CTe" + dadosChave.Chave;
            //cte.infCte.versao = configServico.ObterVersaoLayoutValida();
            cte.infCte.ide.cDV = dadosChave.DigitoVerificador;

            var assinatura = AssinaturaDigital.Assina(cte, cte.infCte.Id, certificate);

            cte.Signature = assinatura;
        }

        public static string Chave(this CteEletronica cte)
        {
            var chave = cte.infCte.Id.Substring(3, 44);
            return chave;
        }

        public static XmlElement QrCode(this XmlDocument doc, X509Certificate2 certificadoDigital,
    Encoding encoding, bool isAdicionaQrCode, string url)
        {
            if (!isAdicionaQrCode) return null;
            if (encoding == null) encoding = Encoding.UTF8;

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("ns", "http://www.portalfiscal.inf.br/cte");

            // 1. Localizar os nós necessários
            var infCte = (XmlElement)doc.SelectSingleNode("//ns:infCte", ns);
            var ide = doc.SelectSingleNode("//ns:ide", ns);

            if (infCte == null || ide == null)
                throw new Exception("Não foi possível localizar as tags infCte ou ide no XML.");

            // 2. Extrair Chave, Ambiente e Tipo de Emissão
            var chave = infCte.GetAttribute("Id").Substring(3, 44);
            var tpAmb = ide.SelectSingleNode("ns:tpAmb", ns).InnerText;
            var tpEmisStr = ide.SelectSingleNode("ns:tpEmis", ns).InnerText;
            int tpEmis = int.Parse(tpEmisStr);

            // 3. Montar a URL do QrCode
            var qrCodeSb = new StringBuilder(url);
            qrCodeSb.Append("?");
            qrCodeSb.Append("chCTe=").Append(chave);
            qrCodeSb.Append("&");
            qrCodeSb.Append("tpAmb=").Append(tpAmb);

            // 4. Lógica de Contingência (Assinatura RSA-SHA1 da chave)
            // Conforme sua lógica original: se não for Normal (1), SVC-RS (7) ou SVC-SP (8)
            if (tpEmis != 1 && tpEmis != 7 && tpEmis != 8)
            {
                var assinatura = Convert.ToBase64String(AssinaturaDigital.CriarAssinaturaPkcs1(certificadoDigital, encoding.GetBytes(chave)));
                qrCodeSb.Append("&sign=").Append(assinatura);
            }

            // 5. Criar o elemento XML <infCTeSupl>
            // IMPORTANTE: Use o Namespace correto da SEFAZ
            XmlElement infCTeSupl = doc.CreateElement("infCTeSupl", "http://www.portalfiscal.inf.br/cte");
            XmlElement qrCodCTe = doc.CreateElement("qrCodCTe", "http://www.portalfiscal.inf.br/cte");
            qrCodCTe.InnerText = qrCodeSb.ToString();

            infCTeSupl.AppendChild(qrCodCTe);

            return infCTeSupl;
        }

        public static infCTeSupl QrCode(this CteEletronica cte, X509Certificate2 certificadoDigital,
            Encoding encoding, bool isAdicionaQrCode, string url)
        {
            if (isAdicionaQrCode == false) return null;

            if (encoding == null)
                encoding = Encoding.UTF8;

            var chave = cte.infCte.Id.Substring(3, 44);

            var qrCode = new StringBuilder(url);
            qrCode.Append("?");
            qrCode.Append("chCTe=").Append(chave);
            qrCode.Append("&");
            qrCode.Append("tpAmb=").Append((int)cte.infCte.ide.tpAmb);

            if (cte.infCte.ide.tpEmis != tpEmis.teNormal
                && cte.infCte.ide.tpEmis != tpEmis.teSVCRS
                && cte.infCte.ide.tpEmis != tpEmis.teSVCSP
                )
            {
                var assinatura = Convert.ToBase64String(AssinaturaDigital.CriarAssinaturaPkcs1(certificadoDigital, encoding.GetBytes(chave)));
                qrCode.Append("&sign=");
                qrCode.Append(assinatura);
            }

            return new infCTeSupl
            {
                qrCodCTe = qrCode.ToString()
            };
        }

        public static void SalvarXmlEmDisco(this CteEletronica cte, ConfiguracaoServico configuracaoServico = null)
        {
            var instanciaServico = configuracaoServico ?? ConfiguracaoServico.Instancia;

            if (instanciaServico.NaoSalvarXml()) return;

            var caminhoXml = instanciaServico.DiretorioSalvarXml;

            var arquivoSalvar = Path.Combine(caminhoXml, cte.Chave() + "-cte.xml");

            FuncoesXml.ClasseParaArquivoXml(cte, arquivoSalvar);
        }

        public static XmlDocument CriaRequestWs(this CteEletronica cte, Estado estado)
        {
            var request = new XmlDocument();

            var xml = cte.ObterXmlString();

            if (estado == Estado.PR
                || estado == Estado.MT)
                //Caso o lote seja enviado para o PR, colocar o namespace nos elementos <CTe> do lote, pois o serviço do PR o exige, conforme https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe/issues/456
                xml = xml.Replace("<CTe>", "<CTe xmlns=\"http://www.portalfiscal.inf.br/cte\">");

            request.LoadXml(xml);

            return request;
        }

        public static void SalvarXmlEmDisco(this retCTe retEnviCte, string chave, ConfiguracaoServico configuracaoServico = null)
        {
            var instanciaServico = configuracaoServico ?? ConfiguracaoServico.Instancia;

            if (instanciaServico.NaoSalvarXml()) return;

            var caminhoXml = instanciaServico.DiretorioSalvarXml;

            var arquivoSalvar = Path.Combine(caminhoXml, chave + "-cte.xml");

            FuncoesXml.ClasseParaArquivoXml(retEnviCte, arquivoSalvar);
        }
    }
}