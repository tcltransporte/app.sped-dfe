using System.Xml;

namespace application.services.Controllers.DFe.NFSe.Providers
{
    public sealed class NFSeProviderResolver
    {
        private readonly IReadOnlyList<INFSeProvider> providers;

        public NFSeProviderResolver(IEnumerable<INFSeProvider> providers)
        {
            this.providers = providers.ToArray();
        }

        public INFSeProvider Resolve(XmlDocument xml)
        {
            var context = CreateContext(xml);
            var provider = providers.FirstOrDefault(item => item.Supports(context));

            if (provider == null)
            {
                var municipio = context.CodigoMunicipio ?? "não identificado";
                throw new InvalidOperationException($"Nenhum provedor de NFS-e configurado para o município {municipio}.");
            }

            return provider;
        }

        private static NFSeProviderContext CreateContext(XmlDocument xml)
        {
            if (xml.DocumentElement == null)
                throw new InvalidOperationException("XML de NFS-e vazio ou inválido.");

            return new NFSeProviderContext
            {
                MunicipioPrestacao = FirstText(xml, "Servico/CodigoMunicipio"),
                MunicipioIncidencia = FirstText(xml, "MunicipioIncidencia"),
                MunicipioPrestador = FirstText(xml, "Prestador/Endereco/CodigoMunicipio"),
                MunicipioTomador = FirstText(xml, "TomadorServico/Endereco/CodigoMunicipio")
            };
        }

        private static string? FirstText(XmlDocument xml, string localNamePath)
        {
            var parts = localNamePath.Split('/');
            var xpath = string.Join("/", parts.Select(part => $"*[local-name()='{part}']"));
            var node = xml.SelectSingleNode($"//{xpath}");

            var value = node?.InnerText?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
