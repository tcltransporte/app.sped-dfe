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
using CTe.Classes.Ext;
using CTe.Servicos.Enderecos.Helpers;
using CTe.Wsdl.ConsultaProtocolo;
using CTe.Wsdl.DistribuicaoDFe;
using CTe.Wsdl.Evento;
using CTe.Wsdl.Inutilizacao;
using CTe.Wsdl.Recepcao;
using CTe.Wsdl.RetRecepcao;
using CTe.Wsdl.Status;
using DFe.Classes.Extensoes;
using CTe.CTeOSDocumento.Common;
using CTe.Wsdl.ConsultaProtocolo.V4;
using CTe.Wsdl.Evento.V4;
using CTe.Wsdl.Recepcao.Sincrono;
using System.Security.Cryptography.X509Certificates;
using DFe.Classes.Flags;
using CTe.Classes.Informacoes.Tipos;
using DFe.Classes.Entidades;
using CTe.Classes.Servicos.Tipos;

namespace CTe.Servicos.Factory
{
    public class WsdlFactory
    {
        public static CteStatusServico CriaWsdlCteStatusServico(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteStatusServico;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteStatusServico(configuracaoWsdl);
        }

        public static CteConsulta CriaWsdlConsultaProtocolo(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteConsulta;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteConsulta(configuracaoWsdl);
        }

        public static CteConsultaV4 CriaWsdlConsultaProtocoloV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteConsulta;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteConsultaV4(configuracaoWsdl);
        }

        public static CteInutilizacao CriaWsdlCteInutilizacao(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteInutilizacao;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteInutilizacao(configuracaoWsdl);
        }

        public static CteRetRecepcao CriaWsdlCteRetRecepcao(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRetRecepcao;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRetRecepcao(configuracaoWsdl);
        }

        public static CteRecepcao CriaWsdlCteRecepcao(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRecepcao;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRecepcao(configuracaoWsdl);
        }

        public static CteRecepcaoSincronoV4 CriaWsdlCteRecepcaoSincronoV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRecepcaoSinc;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRecepcaoSincronoV4(configuracaoWsdl);
        }

        public static CteRecepcaoSincronoOSV4 CriaWsdlCteRecepcaoSincronoOSV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRecepcaoOs;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRecepcaoSincronoOSV4(configuracaoWsdl);
        }

        public static CteRecepcaoEvento CriaWsdlCteEvento(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRecepcaoEvento;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRecepcaoEvento(configuracaoWsdl);
        }

        public static CteRecepcaoEventoV4 CriaWsdlCteEventoV4(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {

            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CteRecepcaoEvento;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CteRecepcaoEventoV4(configuracaoWsdl);
        }

        public static CTeDistDFeInteresse CriaWsdlCTeDistDFeInteresse(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificado)
        {
            var url = UrlHelper.ObterUrlServico(tpAmb, tpEmis, estado, versao).CTeDistribuicaoDFe;

            var configuracaoWsdl = CriaConfiguracao(url, estado, versao, certificado);

            return new CTeDistDFeInteresse(configuracaoWsdl);
        }

        private static WsdlConfiguracao CriaConfiguracao(string url, Estado estado, versao versao, X509Certificate2 certificate)
        {

            var codigoEstado = estado.GetCodigoIbgeEmString();
            var certificadoDigital = certificate;
            var versaoEmString = versao.GetString();
            var timeOut = 0;

            return new WsdlConfiguracao
            {
                CertificadoDigital = certificadoDigital,
                Versao = versaoEmString,
                CodigoIbgeEstado = codigoEstado,
                Url = url,
                TimeOut = timeOut
            };
        }
    }
}