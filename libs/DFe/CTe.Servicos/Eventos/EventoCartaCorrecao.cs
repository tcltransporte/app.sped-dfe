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
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Evento;
using CTe.Classes.Servicos.Evento.Flags;
using CTe.Classes.Servicos.Tipos;
using CTe.Servicos.Factory;
using CTe.Utils.CTe;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CteEletronico = CTe.Classes.CTe;
using CteEletronicoOS = CTe.CTeOSClasses.CTeOS;

namespace CTe.Servicos.Eventos
{
    public class EventoCartaCorrecao
    {
        private readonly CteEletronico _cte;
        private readonly CteEletronicoOS _cteOs;
        private readonly int _sequenciaEvento;
        private readonly List<infCorrecao> _infCorrecaos;

        public eventoCTe EventoEnviado { get; private set; }
        public retEventoCTe RetornoSefaz { get; private set; }

        public EventoCartaCorrecao(CteEletronico cte, int sequenciaEvento,
            List<infCorrecao> infCorrecaos)
        {
            _cte = cte;
            _sequenciaEvento = sequenciaEvento;
            _infCorrecaos = infCorrecaos;
        }

        public EventoCartaCorrecao(CteEletronicoOS cte, int sequenciaEvento, List<infCorrecao> infCorrecaos)
        {
            _cteOs = cte;
            _sequenciaEvento = sequenciaEvento;
            _infCorrecaos = infCorrecaos;
        }

        public retEventoCTe AdicionarCorrecoes(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate)
        {
            var eventoCorrecao = ClassesFactory.CriaEvCCeCTe(_infCorrecaos);

            EventoEnviado = FactoryEvento.CriaEvento(tpAmb, estado, versao, CTeTipoEvento.CartaCorrecao, _sequenciaEvento, _cte.Chave(), _cte.infCte.emit.CNPJ, eventoCorrecao);
            RetornoSefaz = new ServicoController().Executar(tpAmb, tpEmis, estado, versao, certificate, _cte, _sequenciaEvento, eventoCorrecao, CTeTipoEvento.CartaCorrecao);

            return RetornoSefaz;
        }

        public retEventoCTe AdicionarCorrecoesOs(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate)
        {
            var eventoCorrecao = ClassesFactory.CriaEvCCeCTe(_infCorrecaos);

            EventoEnviado = FactoryEvento.CriaEvento(tpAmb, estado, versao, CTeTipoEvento.CartaCorrecao, _sequenciaEvento, _cteOs.Chave(), _cteOs.InfCte.emit.CNPJ, eventoCorrecao);
            RetornoSefaz = new ServicoController().Executar(tpAmb, tpEmis, estado, versao, certificate, _cteOs, _sequenciaEvento, eventoCorrecao, CTeTipoEvento.CartaCorrecao);

            return RetornoSefaz;
        }

        public async Task<retEventoCTe> AdicionarCorrecoesAsync(TipoAmbiente tpAmb, tpEmis tpEmis, Estado estado, versao versao, X509Certificate2 certificate, CTeTipoEvento evento)
        {
            var eventoCorrecao = ClassesFactory.CriaEvCCeCTe(_infCorrecaos);

            EventoEnviado = FactoryEvento.CriaEvento(tpAmb, estado, versao, CTeTipoEvento.CartaCorrecao, _sequenciaEvento, _cte.Chave(), _cte.infCte.emit.CNPJ, eventoCorrecao);
            //RetornoSefaz = await new ServicoController().ExecutarAsync(tpAmb, tpEmis, estado, versao, certificate, evento, _sequenciaEvento, _cte.Chave, eventoCorrecao, CTeTipoEvento.CartaCorrecao);

            //return RetornoSefaz;

            return null;
        }
    }
}