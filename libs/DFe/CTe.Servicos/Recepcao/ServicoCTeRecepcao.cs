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

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using CTe.Classes;
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Recepcao;
using CTe.Classes.Servicos.Tipos;
using CTe.Servicos.Enderecos.Helpers;
using CTe.Servicos.Factory;
using CTe.Utils.CTe;
using CTe.Utils.Recepcao;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils.Assinatura;
using CTeEletronico = CTe.Classes.CTe;

namespace CTe.Servicos.Recepcao
{
    public class ServicoCTeRecepcao
    {
        public event EventHandler<AntesEnviarRecepcao> AntesDeEnviar;

        /*
        public retEnviCte CTeRecepcao(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate, int lote, List<CTeEletronico> cteEletronicosList)
        {
           
            var enviCte = PreparaEnvioCTe(tpAmb, tpEmis, estado, versao, certificate, lote, cteEletronicosList);

            var webService = WsdlFactory.CriaWsdlCteRecepcao(tpAmb, tpEmis, estado, versao, certificate);

            OnAntesDeEnviar(enviCte);

            var retornoXml = webService.cteRecepcaoLote(certificate, enviCte.CriaRequestWs(estado));

            var retorno = retEnviCte.LoadXml(retornoXml.OuterXml, enviCte);
            //retorno.SalvarXmlEmDisco(configServico);

            return retorno;
        }
        */

        /*
        public async Task<retEnviCte> CTeRecepcaoAsync(int lote, List<CTeEletronico> cteEletronicosList)
        {
      
            var enviCte = PreparaEnvioCTe(lote, cteEletronicosList);

            var webService = WsdlFactory.CriaWsdlCteRecepcao(configServico);

            OnAntesDeEnviar(enviCte);

            var retornoXml = await webService.cteRecepcaoLoteAsync(enviCte.CriaRequestWs(configServico));

            var retorno = retEnviCte.LoadXml(retornoXml.OuterXml, enviCte);
            retorno.SalvarXmlEmDisco(configServico);

            return retorno;
        }
        */

        public static XmlNode CTeRecepcaoSincronoV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate, XmlDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("ns", "http://www.portalfiscal.inf.br/cte");
            ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

            // 1. Tratamento de Homologação
            if (tpAmb == TipoAmbiente.Homologacao)
            {
                const string homologa = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
                var remNome = doc.SelectSingleNode("//ns:rem/ns:xNome", ns);
                if (remNome != null) remNome.InnerText = homologa;

                var destNome = doc.SelectSingleNode("//ns:dest/ns:xNome", ns);
                if (destNome != null) destNome.InnerText = homologa;
            }

            // 2. Chama a Assina PRIMEIRO (Gera chave, Id e insere a Signature)
            doc = ExtCTe.Assina(doc, certificate);

            // 3. Gerar e Inserir QR Code
            string urlQrCode = UrlHelper.ObterUrlQrCode(tpAmb, estado, versao);
            XmlElement infCTeSupl = doc.QrCode(certificate, Encoding.UTF8, true, urlQrCode);

            if (infCTeSupl != null)
            {
                // Remove antigo se existir para evitar lixo no XML
                var oldSupl = doc.DocumentElement.SelectSingleNode("ns:infCTeSupl", ns);
                if (oldSupl != null) doc.DocumentElement.RemoveChild(oldSupl);

                doc.DocumentElement.AppendChild(doc.ImportNode(infCTeSupl, true));
            }

            // 4. TRUQUE PROFISSIONAL: Mover a assinatura para depois do QR Code
            // Localizamos a assinatura que a função 'Assina' acabou de criar
            var signatureNode = doc.DocumentElement.SelectSingleNode("ds:Signature", ns);
            if (signatureNode != null)
            {
                // Ao dar AppendChild em um nó existente, ele pula para a ÚLTIMA posição do XML
                doc.DocumentElement.AppendChild(signatureNode);
            }

            // 5. Envio via WebService
            var webService = WsdlFactory.CriaWsdlCteRecepcaoSincronoV4(tpAmb, tpEmis, estado, versao, certificate);
            
            var retornoXml = webService.CTeRecepcaoSincV4(certificate, doc);

            return retornoXml;

        }

        /*
        public retCTe CTeRecepcaoSincronoV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate, CTeEletronico cte)
        {

            //var instanciaConfiguracao = configuracaoServico ?? ConfiguracaoServico.Instancia;

            if (tpAmb == TipoAmbiente.Homologacao)
            {
                const string razaoSocial = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

                cte.infCte.rem.xNome = razaoSocial;
                cte.infCte.dest.xNome = razaoSocial;
            }

            //cte.infCte.ide.tpEmis = instanciaConfiguracao.TipoEmissao;

            //cte.Assina(instanciaConfiguracao);
            cte.infCTeSupl = cte.QrCode(certificate, Encoding.UTF8, true, UrlHelper.ObterUrlQrCode(tpAmb, estado, versao));
            //cte.SalvarXmlEmDisco(instanciaConfiguracao); //salva em disco antes de validas os schemas, facilitando encontrar possíveis erros
            //cte.ValidaSchema(instanciaConfiguracao);
            //cte.SalvarXmlEmDisco(instanciaConfiguracao);

            var webService = WsdlFactory.CriaWsdlCteRecepcaoSincronoV4(tpAmb, tpEmis, estado, versao, certificate);

            //OnAntesDeEnviar(enviCte);

            var retornoXml = webService.CTeRecepcaoSincV4(certificate, cte.CriaRequestWs(estado));

            var retorno = retCTe.LoadXml(retornoXml.OuterXml, cte);
            //retorno.SalvarXmlEmDisco(cte.Chave(), instanciaConfiguracao);

            return retorno;

        }
        */

        /*
        private static enviCTe PreparaEnvioCTe(TipoAmbiente tpAmb, tpEmis tpEmi, Estado estado, versao versao, X509Certificate2 certificate, int lote, List<CTeEletronico> cteEletronicosList)
        {
            //var instanciaConfiguracao = configuracaoServico ?? ConfiguracaoServico.Instancia;

            var enviCte = ClassesFactory.CriaEnviCTe(lote, cteEletronicosList, instanciaConfiguracao);

            if (tpAmb == TipoAmbiente.Homologacao)
            {
                foreach (var cte in enviCte.CTe)
                {
                    const string razaoSocial = "CT-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

                    cte.infCte.rem.xNome = razaoSocial;
                    cte.infCte.dest.xNome = razaoSocial;
                }
            }


            foreach (var cte in enviCte.CTe)
            {
                cte.infCte.ide.tpEmis = tpEmi;
                //cte.Assina(instanciaConfiguracao);
                cte.infCTeSupl = cte.QrCode(certificate, Encoding.UTF8, true, UrlHelper.ObterUrlQrCode(tpAmb, estado, versao));

                //if (configuracaoServico.IsValidaSchemas)
                //    cte.ValidaSchema(instanciaConfiguracao);

                //cte.SalvarXmlEmDisco(instanciaConfiguracao);
            }

            if (configuracaoServico.IsValidaSchemas)
                enviCte.ValidaSchema(instanciaConfiguracao);

            enviCte.SalvarXmlEmDisco(instanciaConfiguracao);
            return enviCte;
        }
        */

        protected virtual void OnAntesDeEnviar(enviCTe enviCTe)
        {
            var handler = AntesDeEnviar;
            if (handler != null) handler(this, new AntesEnviarRecepcao(enviCTe));
        }
    }
}