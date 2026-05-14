using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Servicos.Tipos;
using CTe.Servicos;
using CTe.Servicos.Recepcao;
using CTe.Utils.CTe;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;
using NFe.Classes;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Utils.NFe;
using services.Controllers.DFe.NFe.Schemas;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CT = CTe.Classes;
using DFeUtils = DFe.Utils;
using NFUtils = CTe.Utils;

namespace services.Controllers.DFe.MDFe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class MDFeController : ControllerBase
    {

        
        [HttpPost("generate")]
        [Consumes("application/xml")]
        public IActionResult Generate([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] XmlDocument body)
        {
            try
            {

                var certificate = application.services.Certificate.Loader(base64, password);

                var ns = new XmlNamespaceManager(body.NameTable);
                ns.AddNamespace("n", "http://www.portalfiscal.inf.br/cte");

                // 1. Extra˜˜o de Dados do XML
                var ide = body.SelectSingleNode("//n:ide", ns);
                var infCte = (XmlElement)body.SelectSingleNode("//n:infCte", ns);

                var tpAmb = (TipoAmbiente)int.Parse(ide.SelectSingleNode("n:tpAmb", ns).InnerText);
                var tpEmis = (tpEmis)int.Parse(ide.SelectSingleNode("n:tpEmis", ns).InnerText);
                var cUF = (Estado)int.Parse(ide.SelectSingleNode("n:cUF", ns).InnerText);
                var versao = application.services.Extensions.GetEnumByXmlEnumAttribute<versao>(infCte.GetAttribute("versao"));

                // 2. Chamada do Servi˜o
                var resultado = ServicoCTeRecepcao.CTeRecepcaoSincronoV4(tpAmb, tpEmis, cUF, versao, certificate, body);

                // 3. Verifica˜˜o de Status
                var nsRes = new XmlNamespaceManager(new NameTable());
                nsRes.AddNamespace("n", "http://www.portalfiscal.inf.br/cte");
                var prot = resultado.SelectSingleNode("//n:protCTe", nsRes);
                var cStat = prot?.SelectSingleNode("//n:cStat", nsRes)?.InnerText;

                // 4. Montagem do procCTe (Distribui˜˜o)
                var cteProc = new XmlDocument { PreserveWhitespace = false };
                var root = (XmlElement)cteProc.AppendChild(cteProc.CreateElement("cteProc", "http://www.portalfiscal.inf.br/cte"));
                root.SetAttribute("versao", "4.00");

                root.AppendChild(cteProc.ImportNode(body.DocumentElement, true));
                root.AppendChild(cteProc.ImportNode(prot, true));

                return Content(cteProc.OuterXml, "application/xml", Encoding.UTF8);

            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }


    }

}
