using application.services.Controllers.DFe.NFSe.Providers;
using application.services.Logging;
using Microsoft.AspNetCore.Mvc;
using OpenAC.Net.DFe.Core;
using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.NFSe.Nacional;
using System.Text;
using System.Text.Json;
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

        [HttpPost("consultation-number")]
        public async Task<IActionResult> ConsultationNumber([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] Schemas.ConsultaNumero consultaNumero)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var openNFSeNacional = new OpenNFSeNacional();

                openNFSeNacional.Configuracoes.WebServices.Ambiente = (DFeTipoAmbiente)(consultaNumero.TipoAmbiente ?? 1);
                openNFSeNacional.Configuracoes.Arquivos.PathSchemas = Path.Combine(AppContext.BaseDirectory, "Schemas", "NFSe", "1.00");

                var retorno = await openNFSeNacional.ConsultaChaveDpsAsync(certificate, consultaNumero.numeroDps ?? string.Empty);

                if (retorno.Sucesso)
                {
                    return Ok(new
                    {
                        idDps = retorno.Resultado?.IdDps,
                        chNFSe = retorno.Resultado?.Chave,
                        tpAmb = retorno.Resultado?.Ambiente,
                        dhProcessamento = retorno.Resultado?.DataHoraProcessamento
                    });
                }

                throw new Exception(JsonSerializer.Serialize(retorno.Resultado));
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("consultation-key")]
        public async Task<IActionResult> ConsultationKey([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] Schemas.ConsultaChave consultaChave)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var openNFSeNacional = new OpenNFSeNacional();

                openNFSeNacional.Configuracoes.WebServices.Ambiente = (DFeTipoAmbiente)(consultaChave.TipoAmbiente ?? 1);
                openNFSeNacional.Configuracoes.Arquivos.PathSchemas = Path.Combine(AppContext.BaseDirectory, "Schemas", "NFSe", "1.00");

                var retorno = await openNFSeNacional.ConsultaChaveAsync(certificate, consultaChave.chNFSe ?? string.Empty);

                if (retorno.Sucesso)
                {
                    return Ok(new
                    {
                        chNFSe = retorno.Resultado?.StatusProcessamento,
                        tpAmb = retorno.Resultado?.Ambiente,
                        dhProcessamento = retorno.Resultado?.DataHoraProcessamento,
                        xmlAut = retorno.Resultado?.Lote[0].ArquivoXml
                    });
                }

                throw new Exception(JsonSerializer.Serialize(retorno.Resultado));
            }
            catch (Exception ex)
            {
                this.LogApiError(ex);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("download-danfse")]
        public async Task<IActionResult> DownloadDanfse([FromHeader(Name = "X-Cert-Base64")] string base64, [FromHeader(Name = "X-Cert-Password")] string password, [FromBody] Schemas.ConsultaChave consultaChave)
        {
            try
            {
                var certificate = application.services.Certificate.Loader(base64, password);
                var nacionalProvider = new Providers.Nacional.NacionalNfseProvider(providersConfigStore);

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

