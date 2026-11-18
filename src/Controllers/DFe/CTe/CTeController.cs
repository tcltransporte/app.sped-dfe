using System.Text;
using System.Xml;
using application.services.Controllers.DFe.CTe.Providers;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;

namespace services.Controllers.DFe.CTe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class CTeController : ControllerBase
    {
        private readonly SefazCteProvider cteProvider;
        private readonly CteProvidersXmlStore providersConfigStore;

        public CTeController(SefazCteProvider cteProvider, CteProvidersXmlStore providersConfigStore)
        {
            this.cteProvider = cteProvider;
            this.providersConfigStore = providersConfigStore;
        }

        [HttpGet("providers")]
        public IActionResult GetProviders()
        {
            return Ok(providersConfigStore.GetConfig());
        }

        [HttpPost("generate")]
        [Consumes("application/xml")]
        public async Task<IActionResult> Generate(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] XmlDocument body,
            [FromHeader(Name = "X-CTe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var cteProcXml = await cteProvider.GenerateAsync(certificate, body, requestedProvider);

                return Content(cteProcXml, "application/xml", Encoding.UTF8);
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
            public int? CUF { get; set; }
            public int NSeqEvento { get; set; } = 1;
        }

        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] CancelCteRequest request,
            [FromHeader(Name = "X-CTe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await cteProvider.CancelAsync(
                    certificate,
                    request.ChCTe,
                    request.NProt,
                    request.XJust,
                    request.Cnpj,
                    request.TpAmb,
                    request.CUF,
                    request.NSeqEvento,
                    requestedProvider);

                return Content(resultadoXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("distribuition")]
        public async Task<IActionResult> Distribuition(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] XmlDocument body)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await cteProvider.DistribuitionAsync(certificate, body);

                return Content(resultadoXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
