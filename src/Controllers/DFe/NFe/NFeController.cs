using System.Text;
using System.Xml;
using application.services.Controllers.DFe.NFe.Providers;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;

namespace services.Controllers.DFe.NFe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class NFeController : ControllerBase
    {
        private readonly SefazNfeProvider nfeProvider;
        private readonly NfeProvidersXmlStore providersConfigStore;

        public NFeController(SefazNfeProvider nfeProvider, NfeProvidersXmlStore providersConfigStore)
        {
            this.nfeProvider = nfeProvider;
            this.providersConfigStore = providersConfigStore;
        }

        [HttpGet("providers")]
        public IActionResult GetProviders()
        {
            return Ok(providersConfigStore.GetConfig());
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
                var resultadoXml = await nfeProvider.DistribuitionAsync(certificate, body);

                return Content(resultadoXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("manifestation")]
        public async Task<IActionResult> Manifestation(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] XmlDocument body)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await nfeProvider.ManifestationAsync(certificate, body);

                return Content(resultadoXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("generate")]
        [Consumes("application/xml")]
        public async Task<IActionResult> Generate(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] XmlDocument body,
            [FromHeader(Name = "X-NFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var nfeProcXml = await nfeProvider.GenerateAsync(certificate, body, requestedProvider);

                return Content(nfeProcXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed class CancelNfeRequest
        {
            public string ChNFe { get; set; } = string.Empty;
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
            [FromBody] CancelNfeRequest request,
            [FromHeader(Name = "X-NFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await nfeProvider.CancelAsync(
                    certificate,
                    request.ChNFe,
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
    }
}
