using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace application.services.Controllers.DFe.NFSe.Providers
{
    public interface INFSeProvider
    {
        string Key { get; }

        string Name { get; }

        bool Supports(NFSeProviderContext context);

        Task<string> GenerateAsync(X509Certificate2 certificate, XmlDocument xml);

        Task<string> CancelAsync(X509Certificate2 certificate, XmlDocument xml);
    }
}
