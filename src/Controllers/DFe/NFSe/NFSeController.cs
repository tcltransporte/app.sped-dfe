using application.services.Controllers.DFe.NFSe.Providers;
using application.services.Controllers.DFe.NFSe.Providers.Nacional;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml;

namespace application.services.Controllers.DFe.NFSe
{
    [ApiController]
    [Route("DFe/[controller]")]
    public class NFSeController : ControllerBase
    {
        private readonly NFSeProviderResolver providerResolver;
        private readonly NfseProvidersXmlStore providersConfigStore;

        public NFSeController(NFSeProviderResolver providerResolver, NfseProvidersXmlStore providersConfigStore)
        {
            this.providerResolver = providerResolver;
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
            [FromHeader(Name = "X-NFSe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var provider = providerResolver.Resolve(body, requestedProvider);
                var responseBody = await provider.GenerateAsync(certificate, body);
                var trimmed = responseBody.TrimStart();
                var contentType = trimmed.StartsWith("{") || trimmed.StartsWith("[")
                    ? "application/json"
                    : "application/xml";

                return Content(responseBody, contentType, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("cancel")]
        [Consumes("application/xml")]
        public async Task<IActionResult> Cancel(
            [FromHeader(Name = "X-Cert-Base64")] string base64,
            [FromHeader(Name = "X-Cert-Password")] string password,
            [FromBody] XmlDocument body,
            [FromHeader(Name = "X-NFSe-Provider")] string? requestedProvider = null)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var provider = providerResolver.Resolve(body, requestedProvider);
                var responseBody = await provider.CancelAsync(certificate, body);
                var trimmed = responseBody.TrimStart();
                var contentType = trimmed.StartsWith("{") || trimmed.StartsWith("[")
                    ? "application/json"
                    : "application/xml";

                return Content(responseBody, contentType, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed class ConsultaChave
        {
            public int? TipoAmbiente { get; set; }
            public string? chNFSe { get; set; }
        }

        public sealed class ConsultaNumero
        {
            public int? TipoAmbiente { get; set; }
            public string? numeroDps { get; set; }
        }

        [HttpPost("consultation-number")]
        public async Task<IActionResult> ConsultationNumber([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] ConsultaNumero consultaNumero)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var nacionalProvider = new NacionalNfseProvider(providersConfigStore);
                var resultado = await nacionalProvider.ConsultDpsKeyAsync(
                    certificate,
                    consultaNumero.numeroDps ?? string.Empty,
                    consultaNumero.TipoAmbiente);

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("consultation-key")]
        public async Task<IActionResult> ConsultationKey([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] ConsultaChave consultaChave)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var nacionalProvider = new NacionalNfseProvider(providersConfigStore);
                var resultado = await nacionalProvider.ConsultKeyAsync(
                    certificate,
                    consultaChave.chNFSe ?? string.Empty,
                    consultaChave.TipoAmbiente);

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("download-danfse")]
        public async Task<IActionResult> DownloadDanfse([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] ConsultaChave consultaChave)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var nacionalProvider = new NacionalNfseProvider(providersConfigStore);

                var retorno = await nacionalProvider.DownloadDanfseAsync(
                    certificate,
                    consultaChave.chNFSe ?? string.Empty,
                    consultaChave.TipoAmbiente);

                return Ok(new
                {
                    pdf = Convert.ToBase64String(retorno)
                });
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}

