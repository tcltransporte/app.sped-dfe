using CTe.Classes;
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Tipos;
using CTe.CTeOSDocumento.CTe.CTeOS.Extensoes;
using CTe.CTeOSDocumento.CTe.CTeOS.Servicos.Autorizacao;
using CTe.Servicos.Enderecos.Helpers;
using CTe.Servicos.Factory;
using CTe.Utils.CTe;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CTeEletronico = CTe.CTeOSClasses.CTeOS;

namespace CTe.Servicos.Recepcao
{
    public class ServicoCTeOSRecepcao
    {
        public retCTeOS CTeRecepcaoSincronoV4(TipoAmbiente tpAmb, tpEmis tpEmi, Estado estado, versao versao, X509Certificate2 certificate, CTeEletronico cte)
        {
            //var instanciaConfiguracao = configuracaoServico ?? ConfiguracaoServico.Instancia;

            //cte.InfCte.ide.tpEmis = instanciaConfiguracao.TipoEmissao;
            cte.Assina(certificate);
            cte.infCTeSupl = cte.QrCode(certificate, Encoding.UTF8, true, UrlHelper.ObterUrlQrCode(tpAmb, estado, versao));
            //cte.SalvarXmlEmDisco(instanciaConfiguracao); //salva em disco antes de validas os schemas, facilitando encontrar possíveis erros
            //cte.ValidaSchema(instanciaConfiguracao);
            //cte.SalvarXmlEmDisco(instanciaConfiguracao);

            var webService = WsdlFactory.CriaWsdlCteRecepcaoSincronoOSV4(tpAmb, tpEmi, estado, versao, certificate);

            var retornoXml = webService.CTeRecepcaoSincV4(certificate, cte.CriaRequestWs(estado));

            var retorno = retCTeOS.LoadXml(retornoXml.OuterXml, cte);

            //retorno.SalvarXmlEmDisco(cte.Chave(), configuracaoServico);

            return retorno;
        }
    }
}
