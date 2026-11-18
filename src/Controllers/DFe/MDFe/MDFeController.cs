using System.Text;
using System.Xml;
using application.services.Controllers.DFe.MDFe.Providers;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;

namespace services.Controllers.DFe.MDFe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class MDFeController : ControllerBase
    {
        private readonly SefazMdfeProvider mdfeProvider;
        private readonly MdfeProvidersXmlStore providersConfigStore;

        public MDFeController(SefazMdfeProvider mdfeProvider, MdfeProvidersXmlStore providersConfigStore)
        {
            this.mdfeProvider = mdfeProvider;
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
            [FromHeader(Name = "X-MDFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var mdfeProcXml = await mdfeProvider.GenerateAsync(certificate, body, requestedProvider);

                return Content(mdfeProcXml, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed class CancelMdfeRequest
        {
            public string ChMDFe { get; set; } = string.Empty;
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
            [FromBody] CancelMdfeRequest request,
            [FromHeader(Name = "X-MDFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await mdfeProvider.CancelAsync(
                    certificate,
                    request.ChMDFe,
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

        public sealed class CloseMdfeRequest
        {
            public string ChMDFe { get; set; } = string.Empty;
            public string NProt { get; set; } = string.Empty;
            public DateTime? DtEnc { get; set; }
            public int CUFEncerramento { get; set; }
            public long CMunEncerramento { get; set; }
            public string Cnpj { get; set; } = string.Empty;
            public int TpAmb { get; set; } = 1;
            public int? CUF { get; set; }
            public int NSeqEvento { get; set; } = 1;
        }

        [HttpPost("close")]
        public async Task<IActionResult> Close(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] CloseMdfeRequest request,
            [FromHeader(Name = "X-MDFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await mdfeProvider.CloseAsync(
                    certificate,
                    request.ChMDFe,
                    request.NProt,
                    request.DtEnc,
                    request.CUFEncerramento,
                    request.CMunEncerramento,
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

        public sealed class NonClosedMdfeRequest
        {
            public string Cnpj { get; set; } = string.Empty;
            public int? CUF { get; set; }
            public int TpAmb { get; set; } = 1;
        }

        [HttpPost("nonclosed")]
        public async Task<IActionResult> NonClosed(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] NonClosedMdfeRequest request,
            [FromHeader(Name = "X-MDFe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var resultadoXml = await mdfeProvider.ConsultNonClosedAsync(
                    certificate,
                    request.Cnpj,
                    request.CUF,
                    request.TpAmb,
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
