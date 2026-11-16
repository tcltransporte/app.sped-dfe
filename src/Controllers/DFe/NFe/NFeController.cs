using System.Text;
using System.Xml;
using application.services.Logging;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using Microsoft.AspNetCore.Mvc;
using NFe.Classes;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Servicos;
using NFe.Utils.NFe;
using NF = NFe.Classes;

namespace services.Controllers.DFe.NFe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class NFeController : ControllerBase
    {

        [HttpPost("distribuition")]
        public IActionResult Distribuition([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] XmlDocument body)
        {
            try
            {

                application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

                var certificate = application.services.Certificate.Loader(base64, password);

                // Extrai os valores do XML
                var ufAutor = body.SelectSingleNode("//ufAutor")?.InnerText;
                var documento = body.SelectSingleNode("//documento")?.InnerText;
                var ultNSU = body.SelectSingleNode("//ultNSU")?.InnerText;
                var nSU = body.SelectSingleNode("//nSU")?.InnerText;
                var chNFE = body.SelectSingleNode("//chNFE")?.InnerText;

                if (string.IsNullOrEmpty(ufAutor) || string.IsNullOrEmpty(documento))
                    return BadRequest("ufAutor e documento são obrigatórios.");

                var certBytes = Convert.FromBase64String(base64);

                var resultado = new ServicosNFe().NfeDistDFeInteresse(
                    certificate,
                    ufAutor,
                    documento,
                    ultNSU: ultNSU,
                    nSU: nSU,
                    chNFE: chNFE
                );

                return Content(resultado.OuterXml, "application/xml");
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("manifestation")]
        public IActionResult Manifestation([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] XmlDocument body)
        {
            try
            {

                application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

                var certificate = application.services.Certificate.Loader(base64, password);

                // Extrai os valores do XML
                var documento = body.SelectSingleNode("//documento")?.InnerText;
                var chNFE = body.SelectSingleNode("//chNFE")?.InnerText;

                var servico = new ServicosNFe();

                var resultado = servico.RecepcaoEventoManifestacaoDestinatario(
                    0, 1, chNFE, NF.Servicos.Tipos.NFeTipoEvento.TeMdConfirmacaoDaOperacao, documento, null, DateTime.Now, certificate
                );

                return Content(resultado.OuterXml, "application/xml");

            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("generate")]
        [Consumes("application/xml")]
        public IActionResult Gerenate([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] XmlDocument body)
        {
            try
            {

                application.services.Extensions.LimparCaracteresEspeciais(body.DocumentElement);

                var certificate = application.services.Certificate.Loader(base64, password);

                var ns = new XmlNamespaceManager(body.NameTable);
                ns.AddNamespace("n", "http://www.portalfiscal.inf.br/nfe");

                // 1. Extração dos dados da NFe
                var ide = body.SelectSingleNode("//n:ide", ns);
                var infNFe = (XmlElement)body.SelectSingleNode("//n:infNFe", ns);

                if (ide == null || infNFe == null)
                    return BadRequest(new { message = "XML inválido: ide ou infNFe não encontrado." });

                var tpAmb = (TipoAmbiente)int.Parse(ide.SelectSingleNode("n:tpAmb", ns).InnerText);
                var tpEmis = (TipoEmissao)int.Parse(ide.SelectSingleNode("n:tpEmis", ns).InnerText);
                var cUF = (Estado)int.Parse(ide.SelectSingleNode("n:cUF", ns).InnerText);
                var versao = application.services.Extensions.GetEnumByXmlEnumAttribute<VersaoServico>(infNFe.GetAttribute("versao"));
                var mod = int.Parse(ide.SelectSingleNode("n:mod", ns).InnerText) == 55
                    ? ModeloDocumento.NFe
                    : ModeloDocumento.NFCe;

                // 2. Envio para SEFAZ
                var resultado = new ServicosNFe().NFeAutorizacao(
                    tpAmb,
                    tpEmis,
                    cUF,
                    versao,
                    mod,
                    1,
                    NF.Servicos.Tipos.IndicadorSincronizacao.Sincrono,
                    certificate,
                    body
                );

                // 3. Monta documento da resposta da SEFAZ
                var retornoDoc = new XmlDocument();
                retornoDoc.PreserveWhitespace = false;
                retornoDoc.LoadXml($"<retornoWrapper xmlns=\"http://www.portalfiscal.inf.br/nfe\">{resultado.OuterXml}</retornoWrapper>");

                var nsRet = new XmlNamespaceManager(retornoDoc.NameTable);
                nsRet.AddNamespace("n", "http://www.portalfiscal.inf.br/nfe");

                // tenta localizar protNFe real
                var protNFeNode = retornoDoc.SelectSingleNode("//n:protNFe", nsRet);

                // 4. Monta nfeProc sempre
                var nfeProc = new XmlDocument();
                nfeProc.PreserveWhitespace = true;
                nfeProc.LoadXml($@"<nfeProc xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""{infNFe.GetAttribute("versao")}"" />");

                var root = nfeProc.DocumentElement;

                // 5. Adiciona NFe original
                var nfeOriginal = body.SelectSingleNode("//n:NFe", ns) ?? body.DocumentElement;

                if (nfeOriginal == null)
                    return BadRequest(new { message = "Tag NFe original não encontrada." });

                root?.AppendChild(nfeProc.ImportNode(nfeOriginal, true));

                // 6. Sempre adiciona protNFe
                if (protNFeNode != null)
                {
                    // Se SEFAZ retornou protNFe real, usa ele
                    root?.AppendChild(nfeProc.ImportNode(protNFeNode, true));
                }
                else
                {


                    // pega os campos úteis da SEFAZ
                    var tpAmbNode = retornoDoc.SelectSingleNode("//n:tpAmb", nsRet);
                    var verAplicNode = retornoDoc.SelectSingleNode("//n:verAplic", nsRet);
                    var cStatNode = retornoDoc.SelectSingleNode("//n:cStat", nsRet);
                    var xMotivoNode = retornoDoc.SelectSingleNode("//n:xMotivo", nsRet);
                    var cUFNode = retornoDoc.SelectSingleNode("//n:cUF", nsRet);
                    var chNFeNode = retornoDoc.SelectSingleNode("//n:chNFe", nsRet);
                    var dhRecbtoNode = retornoDoc.SelectSingleNode("//n:dhRecbto", nsRet);
                    var nProtNode = retornoDoc.SelectSingleNode("//n:nProt", nsRet);
                    var digValNode = retornoDoc.SelectSingleNode("//n:digVal", nsRet);


                    // Se não veio protNFe, cria artificialmente
                    var protNFe = nfeProc.CreateElement("protNFe", "http://www.portalfiscal.inf.br/nfe");
                    protNFe.SetAttribute("versao", infNFe.GetAttribute("versao"));

                    var infProt = nfeProc.CreateElement("infProt", "http://www.portalfiscal.inf.br/nfe");

                    if (tpAmbNode != null)
                    {
                        var node = nfeProc.CreateElement("tpAmb", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = tpAmbNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (verAplicNode != null)
                    {
                        var node = nfeProc.CreateElement("verAplic", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = verAplicNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (cStatNode != null)
                    {
                        var node = nfeProc.CreateElement("cStat", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = cStatNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (xMotivoNode != null)
                    {
                        var node = nfeProc.CreateElement("xMotivo", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = xMotivoNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (cUFNode != null)
                    {
                        var node = nfeProc.CreateElement("cUF", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = cUFNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (chNFeNode != null)
                    {
                        var node = nfeProc.CreateElement("chNFe", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = chNFeNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (dhRecbtoNode != null)
                    {
                        var node = nfeProc.CreateElement("dhRecbto", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = dhRecbtoNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (nProtNode != null)
                    {
                        var node = nfeProc.CreateElement("nProt", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = nProtNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    if (digValNode != null)
                    {
                        var node = nfeProc.CreateElement("digVal", "http://www.portalfiscal.inf.br/nfe");
                        node.InnerText = digValNode.InnerText;
                        infProt.AppendChild(node);
                    }

                    protNFe.AppendChild(infProt);
                    root?.AppendChild(protNFe);

                }

                return Content(nfeProc.OuterXml, "application/xml");
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

    }

}