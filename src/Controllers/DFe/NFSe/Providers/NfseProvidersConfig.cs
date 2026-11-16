using System.Xml.Linq;

namespace application.services.Controllers.DFe.NFSe.Providers
{
    public sealed class NfseMunicipalityEntry
    {
        public string Ibge { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Uf { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
    }

    public sealed class NfseProviderConfigEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Standard { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string SoapNamespace { get; init; } = string.Empty;
        public string NfseNamespace { get; init; } = string.Empty;
        public string SoapActionGerar { get; init; } = string.Empty;
        public string SoapActionCancelar { get; init; } = string.Empty;
        public string SoapActionEnviarEvento { get; init; } = string.Empty;
        public string SoapCancelRequestElement { get; init; } = string.Empty;
        public string CancelEventType { get; init; } = string.Empty;
        public string DefaultEndpoint { get; init; } = string.Empty;
        public string DefaultHomologationEndpoint { get; init; } = string.Empty;
        public string ProductionEndpoint { get; init; } = string.Empty;
        public string HomologationEndpoint { get; init; } = string.Empty;
        public string AdnProductionEndpoint { get; init; } = string.Empty;
        public string AdnHomologationEndpoint { get; init; } = string.Empty;
        public string DanfseProductionEndpoint { get; init; } = string.Empty;
        public string DanfseHomologationEndpoint { get; init; } = string.Empty;
        public IReadOnlyList<NfseMunicipalityEntry> Municipalities { get; init; } = Array.Empty<NfseMunicipalityEntry>();
    }

    public sealed class NfseProvidersSnapshot
    {
        public string ConfigFilePath { get; init; } = string.Empty;
        public string DefaultProvider { get; init; } = "ISSNET";
        public IReadOnlyDictionary<string, NfseProviderConfigEntry> Providers { get; init; } =
            new Dictionary<string, NfseProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);

        public NfseProviderConfigEntry? GetProvider(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return Providers.TryGetValue(key.Trim(), out var entry) ? entry : null;
        }

        public bool IsProviderEnabled(string? key)
        {
            var entry = GetProvider(key);
            return entry != null && entry.Enabled;
        }

        public bool SupportsProvider(string key, string? requestedProviderKey, string? codigoMunicipio, IEnumerable<string>? candidateMunicipios = null)
        {
            if (!IsProviderEnabled(key))
                return false;

            if (string.Equals(requestedProviderKey, key, StringComparison.OrdinalIgnoreCase))
                return true;

            var mappedKey = FindProviderKeyByMunicipalities(candidateMunicipios) ?? FindProviderKeyByMunicipality(codigoMunicipio);
            if (!string.IsNullOrWhiteSpace(mappedKey))
                return string.Equals(mappedKey, key, StringComparison.OrdinalIgnoreCase);

            return string.Equals(DefaultProvider, key, StringComparison.OrdinalIgnoreCase);
        }

        public string? FindProviderKeyByMunicipalities(IEnumerable<string>? ibgeCodes)
        {
            if (ibgeCodes == null)
                return null;

            foreach (var code in ibgeCodes)
            {
                var key = FindProviderKeyByMunicipality(code);
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }

            return null;
        }

        public string? FindProviderKeyByMunicipality(string? ibgeCode)
        {
            if (string.IsNullOrWhiteSpace(ibgeCode))
                return null;

            var normalizedIbge = ibgeCode.Trim();

            foreach (var provider in Providers.Values)
            {
                if (!provider.Enabled)
                    continue;

                var match = provider.Municipalities.FirstOrDefault(m =>
                    m.Enabled && string.Equals(m.Ibge, normalizedIbge, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    return provider.Key;
            }

            return null;
        }

        public NfseMunicipalityEntry? FindMunicipality(string providerKey, string? ibgeCode)
        {
            var provider = GetProvider(providerKey);
            if (provider == null || string.IsNullOrWhiteSpace(ibgeCode))
                return null;

            var normalizedIbge = ibgeCode.Trim();
            return provider.Municipalities.FirstOrDefault(m =>
                m.Enabled && string.Equals(m.Ibge, normalizedIbge, StringComparison.OrdinalIgnoreCase));
        }

        public string ResolveIssNetEndpoint(string? ibgeCode, bool useHomologation = false)
        {
            var issNet = GetProvider("ISSNET");

            if (useHomologation)
            {
                return !string.IsNullOrWhiteSpace(issNet?.DefaultHomologationEndpoint)
                    ? issNet.DefaultHomologationEndpoint
                    : !string.IsNullOrWhiteSpace(issNet?.HomologationEndpoint)
                        ? issNet.HomologationEndpoint
                        : "https://nfse.issnetonline.com.br/wsnfsenacional/homologacao/nfse.asmx";
            }

            return !string.IsNullOrWhiteSpace(issNet?.DefaultEndpoint)
                ? issNet.DefaultEndpoint
                : !string.IsNullOrWhiteSpace(issNet?.ProductionEndpoint)
                    ? issNet.ProductionEndpoint
                    : "https://nfse.issnetonline.com.br/wsnfsenacional/goiania/nfse.asmx";
        }

        public string ResolveNacionalEndpoint(bool useHomologation = false)
        {
            var nacional = GetProvider("NACIONAL");

            if (useHomologation)
            {
                return !string.IsNullOrWhiteSpace(nacional?.HomologationEndpoint)
                    ? nacional.HomologationEndpoint
                    : "https://sefin.producaorestrita.nfse.gov.br/SefinNacional/nfse";
            }

            return !string.IsNullOrWhiteSpace(nacional?.ProductionEndpoint)
                ? nacional.ProductionEndpoint
                : "https://sefin.nfse.gov.br/SefinNacional/nfse";
        }

        public string ResolveNacionalDanfseEndpoint(bool useHomologation = false)
        {
            var nacional = GetProvider("NACIONAL");

            if (useHomologation)
            {
                return !string.IsNullOrWhiteSpace(nacional?.DanfseHomologationEndpoint)
                    ? nacional.DanfseHomologationEndpoint
                    : "https://adn.producaorestrita.nfse.gov.br/danfse";
            }

            return !string.IsNullOrWhiteSpace(nacional?.DanfseProductionEndpoint)
                ? nacional.DanfseProductionEndpoint
                : "https://adn.nfse.gov.br/danfse";
        }
    }

    public sealed class NfseProvidersXmlStore
    {
        private readonly object syncLock = new();
        private string? cachedPath;
        private DateTime? cachedLastWriteUtc;
        private NfseProvidersSnapshot? cachedSnapshot;

        public NfseProvidersSnapshot GetConfig()
        {
            var resolvedPath = ResolveConfigFilePath();

            lock (syncLock)
            {
                var currentWriteUtc = resolvedPath != null && File.Exists(resolvedPath)
                    ? File.GetLastWriteTimeUtc(resolvedPath)
                    : (DateTime?)null;

                if (cachedSnapshot != null &&
                    string.Equals(cachedPath, resolvedPath, StringComparison.OrdinalIgnoreCase) &&
                    cachedLastWriteUtc == currentWriteUtc)
                {
                    return cachedSnapshot;
                }

                cachedSnapshot = resolvedPath != null && File.Exists(resolvedPath)
                    ? LoadFromFile(resolvedPath)
                    : CreateFallbackSnapshot();
                cachedPath = resolvedPath;
                cachedLastWriteUtc = currentWriteUtc;

                return cachedSnapshot;
            }
        }

        private static string? ResolveConfigFilePath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Config", "nfse-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Config", "nfse-providers.xml"),
                Path.Combine(AppContext.BaseDirectory, "nfse-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "nfse-providers.xml")
            };

            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        private static NfseProvidersSnapshot LoadFromFile(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root ?? throw new InvalidOperationException($"Arquivo XML de provedores inválido: {filePath}");

            var defaultProvider = (string?)root.Element("DefaultProvider") ?? "ISSNET";
            defaultProvider = string.IsNullOrWhiteSpace(defaultProvider) ? "ISSNET" : defaultProvider.Trim().ToUpperInvariant();

            var providers = new Dictionary<string, NfseProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);

            var providerElements = root.Element("Providers")?.Elements("Provider") ?? Enumerable.Empty<XElement>();
            foreach (var el in providerElements)
            {
                var key = ((string?)el.Attribute("key") ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var municipalities = new List<NfseMunicipalityEntry>();
                var munElements = el.Element("Municipalities")?.Elements("Municipality") ?? Enumerable.Empty<XElement>();
                foreach (var munEl in munElements)
                {
                    var ibge = ((string?)munEl.Attribute("ibge") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(ibge))
                        continue;

                    municipalities.Add(new NfseMunicipalityEntry
                    {
                        Ibge = ibge,
                        Name = ((string?)munEl.Attribute("name") ?? string.Empty).Trim(),
                        Uf = ((string?)munEl.Attribute("uf") ?? string.Empty).Trim(),
                        Enabled = ParseBool((string?)munEl.Attribute("enabled"), true)
                    });
                }

                providers[key] = new NfseProviderConfigEntry
                {
                    Key = key,
                    Name = ((string?)el.Attribute("name") ?? key).Trim(),
                    Standard = ((string?)el.Attribute("standard") ?? string.Empty).Trim(),
                    Version = ((string?)el.Attribute("version") ?? string.Empty).Trim(),
                    Enabled = ParseBool((string?)el.Attribute("enabled"), true),
                    SoapNamespace = ((string?)el.Element("SoapNamespace") ?? string.Empty).Trim(),
                    NfseNamespace = ((string?)el.Element("NfseNamespace") ?? string.Empty).Trim(),
                    SoapActionGerar = ((string?)el.Element("SoapActionGerar") ?? string.Empty).Trim(),
                    SoapActionCancelar = ((string?)el.Element("SoapActionCancelar") ?? string.Empty).Trim(),
                    SoapActionEnviarEvento = ((string?)el.Element("SoapActionEnviarEvento") ?? string.Empty).Trim(),
                    SoapCancelRequestElement = ((string?)el.Element("SoapCancelRequestElement") ?? string.Empty).Trim(),
                    CancelEventType = ((string?)el.Element("CancelEventType") ?? string.Empty).Trim(),
                    DefaultEndpoint = ((string?)el.Element("DefaultEndpoint") ?? string.Empty).Trim(),
                    DefaultHomologationEndpoint = ((string?)el.Element("DefaultHomologationEndpoint") ?? string.Empty).Trim(),
                    ProductionEndpoint = ((string?)el.Element("ProductionEndpoint") ?? string.Empty).Trim(),
                    HomologationEndpoint = ((string?)el.Element("HomologationEndpoint") ?? string.Empty).Trim(),
                    AdnProductionEndpoint = ((string?)el.Element("AdnProductionEndpoint") ?? string.Empty).Trim(),
                    AdnHomologationEndpoint = ((string?)el.Element("AdnHomologationEndpoint") ?? string.Empty).Trim(),
                    DanfseProductionEndpoint = ((string?)el.Element("DanfseProductionEndpoint") ?? string.Empty).Trim(),
                    DanfseHomologationEndpoint = ((string?)el.Element("DanfseHomologationEndpoint") ?? string.Empty).Trim(),
                    Municipalities = municipalities
                };
            }

            if (providers.Count == 0)
                return CreateFallbackSnapshot(filePath);

            return new NfseProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = defaultProvider,
                Providers = providers
            };
        }

        private static NfseProvidersSnapshot CreateFallbackSnapshot(string filePath = "")
        {
            var providers = new Dictionary<string, NfseProviderConfigEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["ISSNET"] = new NfseProviderConfigEntry
                {
                    Key = "ISSNET",
                    Name = "ISS.NET (Padrão Nacional v1.01)",
                    Standard = "NACIONAL",
                    Version = "1.01",
                    Enabled = true,
                    SoapNamespace = "http://www.sped.fazenda.gov.br/nfse",
                    NfseNamespace = "http://www.sped.fazenda.gov.br/nfse",
                    SoapActionGerar = "http://www.sped.fazenda.gov.br/nfse/GerarNfse",
                    SoapActionCancelar = "http://www.sped.fazenda.gov.br/nfse/CancelarNfse",
                    SoapCancelRequestElement = "CancelarNfse",
                    DefaultEndpoint = "https://nfse.issnetonline.com.br/wsnfsenacional/goiania/nfse.asmx",
                    DefaultHomologationEndpoint = "https://nfse.issnetonline.com.br/wsnfsenacional/homologacao/nfse.asmx",
                    Municipalities = new[]
                    {
                        new NfseMunicipalityEntry
                        {
                            Ibge = "5208707",
                            Name = "Goiânia",
                            Uf = "GO",
                            Enabled = true
                        }
                    }
                },
                ["NACIONAL"] = new NfseProviderConfigEntry
                {
                    Key = "NACIONAL",
                    Name = "NFS-e Nacional (SEFIN/ADN)",
                    Standard = "NACIONAL",
                    Version = "1.01",
                    Enabled = true,
                    ProductionEndpoint = "https://sefin.nfse.gov.br/SefinNacional/nfse",
                    HomologationEndpoint = "https://sefin.producaorestrita.nfse.gov.br/SefinNacional/nfse",
                    AdnProductionEndpoint = "https://adn.nfse.gov.br/contribuintes",
                    AdnHomologationEndpoint = "https://adn.producaorestrita.nfse.gov.br/contribuintes",
                    DanfseProductionEndpoint = "https://adn.nfse.gov.br/danfse",
                    DanfseHomologationEndpoint = "https://adn.producaorestrita.nfse.gov.br/danfse",
                    Municipalities = new[]
                    {
                        new NfseMunicipalityEntry
                        {
                            Ibge = "5108402",
                            Name = "Várzea Grande",
                            Uf = "MT",
                            Enabled = true
                        }
                    }
                }
            };

            return new NfseProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = "ISSNET",
                Providers = providers
            };
        }

        private static bool ParseBool(string? value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return bool.TryParse(value.Trim(), out var parsed) ? parsed : fallback;
        }
    }
}

