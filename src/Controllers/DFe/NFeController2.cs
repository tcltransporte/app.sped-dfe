using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using application.services.Logging;
using application.services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace application.services.Controllers;

[ApiController]
[Route("dfe/nfe")]
public class NFeController2 : ControllerBase
{
   

    /// <summary>
    /// Receives the <c>distDFeInt</c> payload (application/xml body), wraps it in the NFeDistribuicaoDFe SOAP envelope, and posts to SEFAZ.
    /// </summary>
    /// <param name="certBase64">PFX in Base64 (header <c>X-Cert-Base64</c>).</param>
    /// <param name="certPassword">PFX password (header <c>X-Cert-Password</c>).</param>
    [HttpPost("distribuicao-dfe")]
    [Consumes("application/xml", "text/xml")]
    public IActionResult NFeDistDFeInteresse(
        [FromServices] SefazSoapService sefazSoapService,
        [FromServices] IWebHostEnvironment environment,
        [FromHeader(Name = "X-Cert-Base64")] string certBase64,
        [FromHeader(Name = "X-Cert-Password")] string certPassword,
        [FromBody] XmlDocument body)
    {
        try
        {

            var NFeDistribuicaoDfeSoapAction = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe/nfeDistDFeInteresse";
            var DistributionServiceName = "NFeDistribuicaoDFe";
            var NationalUfCode = "AN";

            var tpAmb = body.SelectSingleNode("//*[local-name()='tpAmb']");

            if (string.IsNullOrWhiteSpace(certBase64))
                return BadRequest(new { message = "Certificate was not provided (X-Cert-Base64)." });

            if (body?.DocumentElement is null || !string.Equals(body.DocumentElement.LocalName, "distDFeInt", StringComparison.Ordinal))
                return BadRequest(new { message = "Request body root element must be distDFeInt." });

            if (tpAmb is null || string.IsNullOrWhiteSpace(tpAmb.InnerText))
                return BadRequest(new { message = "tpAmb was not provided in distDFeInt." });

            var sefazUrl = ResolveEndpointUrl(environment, uf: NationalUfCode, service: DistributionServiceName, tpAmb: tpAmb.InnerText.Trim());

            certBase64 = certBase64.Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
            
            var pfxBytes = Convert.FromBase64String(certBase64);
            
            using var certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, certPassword);

            var soapEnvelope = BuildNFeDistribuicaoDfeSoapEnvelope(body);
            
            var responseXml = sefazSoapService.SendRequest(sefazUrl, NFeDistribuicaoDfeSoapAction, certificate, soapEnvelope);

            var retDistDFeInt = ExtractRetDistDFeInt(responseXml);
            
            if (retDistDFeInt is null)
                return BadRequest(new { message = "retDistDFeInt was not found in the SEFAZ SOAP response." });

            return Content(retDistDFeInt.OuterXml, "application/xml", Encoding.UTF8);

        }
        catch (XmlException ex)
        {
            this.LogApiError(ex);
            return BadRequest(new { message = "Invalid XML.", detail = ex.Message });
        }
        catch (CryptographicException ex)
        {
            this.LogApiError(ex);
            return BadRequest(new { message = "Invalid certificate password or corrupted PFX." });
        }
        catch (HttpRequestException ex)
        {
            this.LogApiError(ex);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            this.LogApiError(ex);
            return BadRequest(new { message = ex.Message });
        }
    }

    private static XmlDocument BuildNFeDistribuicaoDfeSoapEnvelope(XmlDocument distDFeInt)
    {

        string NFeDistribuicaoWsdlNamespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe";
        string SoapEnvelopeNamespace = "http://schemas.xmlsoap.org/soap/envelope/";

        var doc = new XmlDocument();
        var declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(declaration);

        var envelope = doc.CreateElement("soap", "Envelope", SoapEnvelopeNamespace);
        envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
        envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        doc.AppendChild(envelope);

        var body = doc.CreateElement("soap", "Body", SoapEnvelopeNamespace);
        envelope.AppendChild(body);

        var nfeDistDFeInteresse = doc.CreateElement("nfeDistDFeInteresse", NFeDistribuicaoWsdlNamespace);
        body.AppendChild(nfeDistDFeInteresse);

        var nfeDadosMsg = doc.CreateElement("nfeDadosMsg", NFeDistribuicaoWsdlNamespace);
        nfeDistDFeInteresse.AppendChild(nfeDadosMsg);

        var imported = doc.ImportNode(distDFeInt.DocumentElement!, deep: true);
        nfeDadosMsg.AppendChild(imported);

        return doc;
    }

    /// <summary>
    /// Resolves SEFAZ URL from <c>SefazEndpoints.xml</c> using UF, service name, and environment (<c>tpAmb</c>: 1 production, 2 test).
    /// </summary>
    private static string ResolveEndpointUrl(IWebHostEnvironment environment, string uf, string service, string tpAmb)
    {
        if (string.IsNullOrWhiteSpace(uf))
            throw new ArgumentException("UF is required.", nameof(uf));
        if (string.IsNullOrWhiteSpace(service))
            throw new ArgumentException("Service is required.", nameof(service));

        var endpointsPath = Path.Combine(environment.ContentRootPath, "Services", "SefazEndpoints.xml");
        if (!System.IO.File.Exists(endpointsPath))
            throw new FileNotFoundException($"SEFAZ endpoints file was not found: {endpointsPath}");

        var configDoc = new XmlDocument();
        configDoc.Load(endpointsPath);

        var ufEscaped = EscapeXPathStringLiteral(uf.Trim());
        var serviceEscaped = EscapeXPathStringLiteral(service.Trim());
        var endpointNode = configDoc.SelectSingleNode(
            $"/SefazConfig/Endpoints/Endpoint[@uf={ufEscaped} and @service={serviceEscaped}]");
        if (endpointNode is null)
            throw new InvalidOperationException(
                $"No endpoint configured for uf={uf.Trim()} service={service.Trim()} in SefazEndpoints.xml.");

        var url = tpAmb switch
        {
            "1" => endpointNode.SelectSingleNode("ProductionUrl")?.InnerText?.Trim(),
            "2" => endpointNode.SelectSingleNode("TestUrl")?.InnerText?.Trim(),
            _ => throw new InvalidOperationException("tpAmb must be 1 (production) or 2 (test).")
        };

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("SEFAZ URL for the selected environment was not configured.");

        return url;
    }

    private static string EscapeXPathStringLiteral(string value)
    {
        if (value.Contains('\'', StringComparison.Ordinal))
            throw new ArgumentException("Value must not contain single quotes for XPath safety.", nameof(value));
        return $"'{value}'";
    }

    private static XmlElement? ExtractRetDistDFeInt(XmlDocument soapResponse)
    {
        var node = soapResponse.SelectSingleNode("//*[local-name()='retDistDFeInt']") as XmlElement;
        return node;
    }
}
