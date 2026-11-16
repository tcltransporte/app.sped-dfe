using System.Text;
using System.Xml;
using application.services.Logging;
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Evento.Flags;
using CTe.Classes.Servicos.Tipos;
using CTe.Servicos.Eventos;
using CTe.Servicos.Factory;
using CTe.Servicos.Recepcao;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using Microsoft.AspNetCore.Mvc;

namespace services.Controllers.DFe.CTe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class CTeController : ControllerBase
    {

        [HttpPost("generate")]
        [Consumes("application/xml")]
        public IActionResult Generate([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] XmlDocument body)
        {
            try
            {
                // IMPORTANTE: a assinatura digital (ExtCTe.Assina -> AssinaturaDigital.Assina)
                // remove acentos da COPIA do XML que ela usa para calcular o digest, mas devolve
                // a Signature para ser anexada no DOC ORIGINAL. Se o doc original tiver acentos,
                // o digest da Signature nao vai bater com o conteudo enviado e a SEFAZ rejeita
                // com cStat 297 "Assinatura difere do calculado". Por isso normalizamos o body
                // ANTES de assinar, garantindo que doc assinado e doc enviado sejam identicos.
                application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

                var certificate = application.services.Certificate.Loader(base64, password);

                var ns = new XmlNamespaceManager(body.NameTable);
                ns.AddNamespace("n", "http://www.portalfiscal.inf.br/cte");

                // 1. Extracao de dados do XML
                var ide = body.SelectSingleNode("//n:ide", ns);
                var infCte = (XmlElement)body.SelectSingleNode("//n:infCte", ns);

                var tpAmb = (TipoAmbiente)int.Parse(ide.SelectSingleNode("n:tpAmb", ns).InnerText);
                var tpEmis = (tpEmis)int.Parse(ide.SelectSingleNode("n:tpEmis", ns).InnerText);
                var cUF = (Estado)int.Parse(ide.SelectSingleNode("n:cUF", ns).InnerText);
                var versao = application.services.Extensions.GetEnumByXmlEnumAttribute<versao>(infCte.GetAttribute("versao"));

                // 2. Chamada do servico
                var resultado = ServicoCTeRecepcao.CTeRecepcaoSincronoV4(tpAmb, tpEmis, cUF, versao, certificate, body);

                // 3. Montagem do procCTe (distribuicao)
                var nsRes = new XmlNamespaceManager(new NameTable());
                nsRes.AddNamespace("n", "http://www.portalfiscal.inf.br/cte");
                var prot = resultado.SelectSingleNode("//n:protCTe", nsRes);

                var cteProc = new XmlDocument { PreserveWhitespace = false };
                var root = (XmlElement)cteProc.AppendChild(cteProc.CreateElement("cteProc", "http://www.portalfiscal.inf.br/cte"));
                root.SetAttribute("versao", "4.00");

                root.AppendChild(cteProc.ImportNode(body.DocumentElement, true));

                // retCTe sem protCTe (ex. cStat 908): ImportNode(null) quebra; sintetiza infProt.
                XmlNode protParaAnexar;
                if (prot != null)
                    protParaAnexar = cteProc.ImportNode(prot, true);
                else
                    protParaAnexar = BuildProtCteFromRetSemProt(resultado, infCte, cteProc, nsRes);

                root.AppendChild(protParaAnexar);

                return Content(cteProc.OuterXml, "application/xml", Encoding.UTF8);

            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed class CancelCteRequest
        {
            public string ChCTe { get; set; } = string.Empty;
            public string NProt { get; set; } = string.Empty;
            public string XJust { get; set; } = string.Empty;
            public string Cnpj { get; set; } = string.Empty;
            public int TpAmb { get; set; } = 1;
            public int CUF { get; set; } = 51;
            public int NSeqEvento { get; set; } = 1;
        }

        [HttpPost("cancel")]
        public IActionResult Cancel(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] CancelCteRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ChCTe) || string.IsNullOrWhiteSpace(request.NProt))
                    return BadRequest(new { message = "Chave do CT-e e protocolo de autorização são obrigatórios." });

                var justificativa = (request.XJust ?? string.Empty).Trim();
                if (justificativa.Length < 15)
                    return BadRequest(new { message = "A justificativa do cancelamento deve ter no mínimo 15 caracteres." });

                var certificate = application.services.Certificate.Loader(base64, password);
                var evento = ClassesFactory.CriaEvCancCTe(justificativa, request.NProt.Trim());
                var servico = new ServicoController();
                var retorno = servico.Executar(
                    (TipoAmbiente)request.TpAmb,
                    tpEmis.teNormal,
                    (Estado)request.CUF,
                    versao.ve400,
                    certificate,
                    CTeTipoEvento.Cancelamento,
                    request.NSeqEvento > 0 ? request.NSeqEvento : 1,
                    request.ChCTe.Trim(),
                    request.Cnpj.Trim(),
                    evento);

                var xml = FuncoesXml.ClasseParaXmlString(retorno);
                return Content(xml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        private const string CteNs = "http://www.portalfiscal.inf.br/cte";

        // Quando o WS retorna retCTe sem protCTe, monta protCTe/infProt no leiaute do protocolo (TProtCTe v4).
        private static XmlElement BuildProtCteFromRetSemProt(XmlNode retCte, XmlElement infCte, XmlDocument owner, XmlNamespaceManager ns)
        {
            if (retCte == null) throw new ArgumentNullException(nameof(retCte));
            if (infCte == null) throw new ArgumentNullException(nameof(infCte));
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            var retEl = retCte as XmlElement ?? retCte.OwnerDocument?.DocumentElement;
            var retVersao = retEl?.GetAttribute("versao");
            if (string.IsNullOrEmpty(retVersao))
                retVersao = infCte.GetAttribute("versao");
            if (string.IsNullOrEmpty(retVersao))
                retVersao = "4.00";

            string LocalText(string localName)
            {
                var n = retCte.SelectSingleNode($".//n:{localName}", ns);
                return n?.InnerText ?? string.Empty;
            }

            var idAttr = infCte.GetAttribute("Id") ?? string.Empty;
            string chave;
            if (idAttr.Length >= 47 && idAttr.StartsWith("CTe", StringComparison.OrdinalIgnoreCase))
                chave = idAttr.Substring(3, 44);
            else if (idAttr.Length >= 44)
                chave = idAttr.Length > 44 ? idAttr.Substring(0, 44) : idAttr;
            else
                chave = idAttr;

            var prot = owner.CreateElement("protCTe", CteNs);
            prot.SetAttribute("versao", retVersao);

            var infProt = owner.CreateElement("infProt", CteNs);
            prot.AppendChild(infProt);

            void Append(string localName, string text)
            {
                var el = owner.CreateElement(localName, CteNs);
                el.InnerText = text ?? string.Empty;
                infProt.AppendChild(el);
            }

            Append("tpAmb", LocalText("tpAmb"));
            Append("verAplic", LocalText("verAplic"));
            Append("chCTe", chave);
            // retCTe sem prot nao traz dhRecbto; schema exige em infProt: timestamp da resposta na API.
            Append("dhRecbto", DateTimeOffset.Now.ParaDataHoraStringUtc());

            var infProtNoRet = retCte.SelectSingleNode(".//n:infProt", ns);
            if (infProtNoRet != null)
            {
                var nProt = infProtNoRet.SelectSingleNode("n:nProt", ns);
                if (nProt != null && !string.IsNullOrEmpty(nProt.InnerText))
                    Append("nProt", nProt.InnerText);
                var digVal = infProtNoRet.SelectSingleNode("n:digVal", ns);
                if (digVal != null && !string.IsNullOrEmpty(digVal.InnerText))
                    Append("digVal", digVal.InnerText);
                var cStatNode = infProtNoRet.SelectSingleNode("n:cStat", ns);
                var xMotivoNode = infProtNoRet.SelectSingleNode("n:xMotivo", ns);
                Append("cStat", cStatNode?.InnerText ?? LocalText("cStat"));
                Append("xMotivo", xMotivoNode?.InnerText ?? LocalText("xMotivo"));
            }
            else
            {
                Append("cStat", LocalText("cStat"));
                Append("xMotivo", LocalText("xMotivo"));
            }

            return prot;
        }

    }

}
