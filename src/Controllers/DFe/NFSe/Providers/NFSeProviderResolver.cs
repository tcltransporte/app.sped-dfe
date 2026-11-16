using System.Xml;

namespace application.services.Controllers.DFe.NFSe.Providers
{
    public sealed class NFSeProviderResolver
    {
        private readonly IReadOnlyList<INFSeProvider> providers;
        private readonly NfseProvidersXmlStore configStore;

        public NFSeProviderResolver(IEnumerable<INFSeProvider> providers, NfseProvidersXmlStore configStore)
        {
            this.providers = providers.ToArray();
            this.configStore = configStore;
        }

        public INFSeProvider Resolve(XmlDocument xml, string? requestedProviderKey = null)
        {
            var config = configStore.GetConfig();
            var context = CreateContext(xml, requestedProviderKey);

            INFSeProvider? FindEnabledByKey(string? key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return null;

                var normalizedKey = key.Trim();
                if (!config.IsProviderEnabled(normalizedKey))
                    return null;

                return providers.FirstOrDefault(p =>
                    string.Equals(p.Key, normalizedKey, StringComparison.OrdinalIgnoreCase) &&
                    p.Supports(context));
            }

            var mappedProviderKey =
                config.FindProviderKeyByMunicipalities(context.CandidateMunicipios) ??
                config.FindProviderKeyByMunicipality(context.CodigoMunicipio);

            var provider =
                FindEnabledByKey(context.RequestedProviderKey) ??
                FindEnabledByKey(mappedProviderKey) ??
                FindEnabledByKey(config.DefaultProvider) ??
                providers.FirstOrDefault(item => config.IsProviderEnabled(item.Key) && item.Supports(context));

            if (provider == null)
            {
                var municipio = context.CodigoMunicipio ?? "não identificado";
                throw new InvalidOperationException($"Nenhum provedor de NFS-e configurado para o município {municipio}.");
            }

            return provider;
        }

        private static NFSeProviderContext CreateContext(XmlDocument xml, string? requestedProviderKey)
        {
            if (xml.DocumentElement == null)
                throw new InvalidOperationException("XML de NFS-e vazio ou inválido.");

            var chNfse = FirstText(xml, "chNFSe");
            var munFromKey = !string.IsNullOrWhiteSpace(chNfse) && chNfse.Length >= 7 && chNfse.Take(7).All(char.IsDigit)
                ? chNfse[..7]
                : null;

            var infId =
                (xml.SelectSingleNode("//*[local-name()='infDPS']") as XmlElement)?.GetAttribute("Id") ??
                (xml.SelectSingleNode("//*[local-name()='infPedReg']") as XmlElement)?.GetAttribute("Id") ??
                (xml.SelectSingleNode("//*[local-name()='infNFSe']") as XmlElement)?.GetAttribute("Id");

            var munFromId = !string.IsNullOrWhiteSpace(infId) &&
                            infId.Length >= 10 &&
                            (infId.StartsWith("DPS", StringComparison.OrdinalIgnoreCase) ||
                             infId.StartsWith("PRE", StringComparison.OrdinalIgnoreCase) ||
                             infId.StartsWith("NFS", StringComparison.OrdinalIgnoreCase)) &&
                            infId.Substring(3, 7).All(char.IsDigit)
                ? infId.Substring(3, 7)
                : null;

            var munEmissor =
                FirstText(xml, "cLocEmi") ??
                FirstText(xml, "Prestador/Endereco/CodigoMunicipio") ??
                FirstText(xml, "IdentificacaoNfse/CodigoMunicipio") ??
                munFromKey ??
                munFromId;

            return new NFSeProviderContext
            {
                RootLocalName = xml.DocumentElement.LocalName,
                RequestedProviderKey = string.IsNullOrWhiteSpace(requestedProviderKey) ? null : requestedProviderKey.Trim().ToUpperInvariant(),
                IsDpsXml = string.Equals(xml.DocumentElement.LocalName, "DPS", StringComparison.OrdinalIgnoreCase) ||
                           xml.SelectSingleNode("//*[local-name()='infDPS']") != null,
                MunicipioPrestacao = FirstText(xml, "Servico/CodigoMunicipio") ?? FirstText(xml, "locPrest/cLocPrestacao") ?? FirstText(xml, "IdentificacaoNfse/CodigoMunicipio") ?? munFromKey ?? munFromId,
                MunicipioIncidencia = FirstText(xml, "MunicipioIncidencia") ?? FirstText(xml, "cLocIncid") ?? FirstText(xml, "cLocEmi") ?? munFromKey ?? munFromId,
                MunicipioPrestador = munEmissor,
                MunicipioTomador = FirstText(xml, "TomadorServico/Endereco/CodigoMunicipio") ?? FirstText(xml, "toma/end/endNac/cMun")
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
