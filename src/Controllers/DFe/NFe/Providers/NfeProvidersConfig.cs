using System.Xml.Linq;

namespace application.services.Controllers.DFe.NFe.Providers
{
    public sealed class NfeStateEntry
    {
        public string CUF { get; init; } = string.Empty;
        public string Uf { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
    }

    public sealed class NfeServiceEndpoints
    {
        public string NFeAutorizacao { get; init; } = string.Empty;
        public string NFeRetAutorizacao { get; init; } = string.Empty;
        public string NFeRecepcaoEvento { get; init; } = string.Empty;
        public string NFeConsultaProtocolo { get; init; } = string.Empty;
        public string NFeStatusServico { get; init; } = string.Empty;
        public string NFeInutilizacao { get; init; } = string.Empty;
    }

    public sealed class NfeNationalEnvironmentEndpoints
    {
        public string DistribuicaoDFeProduction { get; init; } = "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";
        public string DistribuicaoDFeHomologation { get; init; } = "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";
        public string RecepcaoEventoProduction { get; init; } = "https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        public string RecepcaoEventoHomologation { get; init; } = "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
    }

    public sealed class NfeProviderConfigEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = "4.00";
        public bool Enabled { get; init; } = true;
        public NfeServiceEndpoints Production { get; init; } = new();
        public NfeServiceEndpoints Homologation { get; init; } = new();
        public IReadOnlyList<NfeStateEntry> States { get; init; } = Array.Empty<NfeStateEntry>();
    }

    public sealed class NfeProvidersSnapshot
    {
        public string ConfigFilePath { get; init; } = string.Empty;
        public string DefaultProvider { get; init; } = "SVRS";
        public NfeNationalEnvironmentEndpoints NationalEnvironment { get; init; } = new();
        public IReadOnlyDictionary<string, NfeProviderConfigEntry> Providers { get; init; } =
            new Dictionary<string, NfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);

        public NfeProviderConfigEntry? GetProvider(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return Providers.TryGetValue(key.Trim(), out var entry) && entry.Enabled ? entry : null;
        }

        public NfeProviderConfigEntry ResolveProvider(string? ufOrCuf, string? requestedProviderKey = null)
        {
            if (!string.IsNullOrWhiteSpace(requestedProviderKey))
            {
                var explicitProvider = GetProvider(requestedProviderKey);
                if (explicitProvider != null)
                    return explicitProvider;
            }

            if (!string.IsNullOrWhiteSpace(ufOrCuf))
            {
                var normalized = ufOrCuf.Trim();
                foreach (var provider in Providers.Values)
                {
                    if (!provider.Enabled)
                        continue;

                    var match = provider.States.FirstOrDefault(s =>
                        s.Enabled &&
                        (string.Equals(s.CUF, normalized, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(s.Uf, normalized, StringComparison.OrdinalIgnoreCase)));

                    if (match != null)
                        return provider;
                }
            }

            return GetProvider(DefaultProvider)
                ?? Providers.Values.FirstOrDefault(p => p.Enabled)
                ?? throw new InvalidOperationException($"Nenhum provedor SEFAZ de NF-e configurado para a UF '{ufOrCuf}'.");
        }

        public NfeServiceEndpoints ResolveEndpoints(string? ufOrCuf, bool useHomologation, string? requestedProviderKey = null)
        {
            var provider = ResolveProvider(ufOrCuf, requestedProviderKey);
            return useHomologation ? provider.Homologation : provider.Production;
        }

        public string ResolveDistribuicaoEndpoint(bool useHomologation)
        {
            return useHomologation
                ? NationalEnvironment.DistribuicaoDFeHomologation
                : NationalEnvironment.DistribuicaoDFeProduction;
        }

        public string ResolveNationalEventoEndpoint(bool useHomologation)
        {
            return useHomologation
                ? NationalEnvironment.RecepcaoEventoHomologation
                : NationalEnvironment.RecepcaoEventoProduction;
        }
    }

    public sealed class NfeProvidersXmlStore
    {
        private readonly object syncLock = new();
        private string? cachedPath;
        private DateTime? cachedLastWriteUtc;
        private NfeProvidersSnapshot? cachedSnapshot;

        public NfeProvidersSnapshot GetConfig()
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
                    : CreateFallbackSnapshot(resolvedPath ?? string.Empty);
                cachedPath = resolvedPath;
                cachedLastWriteUtc = currentWriteUtc;

                return cachedSnapshot;
            }
        }

        private static string? ResolveConfigFilePath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Config", "nfe-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Config", "nfe-providers.xml"),
                Path.Combine(AppContext.BaseDirectory, "nfe-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "nfe-providers.xml")
            };

            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        private static NfeProvidersSnapshot LoadFromFile(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root ?? throw new InvalidOperationException($"Arquivo XML de provedores NF-e inválido: {filePath}");

            var defaultProvider = ((string?)root.Element("DefaultProvider") ?? "SVRS").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(defaultProvider))
                defaultProvider = "SVRS";

            var natEl = root.Element("NationalEnvironment");
            var nationalEnv = new NfeNationalEnvironmentEndpoints
            {
                DistribuicaoDFeProduction = ReadElement(natEl, "DistribuicaoDFeProduction", "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx"),
                DistribuicaoDFeHomologation = ReadElement(natEl, "DistribuicaoDFeHomologation", "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx"),
                RecepcaoEventoProduction = ReadElement(natEl, "RecepcaoEventoProduction", "https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"),
                RecepcaoEventoHomologation = ReadElement(natEl, "RecepcaoEventoHomologation", "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx")
            };

            var providers = new Dictionary<string, NfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);
            var providerElements = root.Element("Providers")?.Elements("Provider") ?? Enumerable.Empty<XElement>();

            foreach (var el in providerElements)
            {
                var key = ((string?)el.Attribute("key") ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var states = new List<NfeStateEntry>();
                var stateElements = el.Element("States")?.Elements("State") ?? Enumerable.Empty<XElement>();
                foreach (var stEl in stateElements)
                {
                    var cUF = ((string?)stEl.Attribute("cUF") ?? string.Empty).Trim();
                    var uf = ((string?)stEl.Attribute("uf") ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(cUF) && string.IsNullOrWhiteSpace(uf))
                        continue;

                    states.Add(new NfeStateEntry
                    {
                        CUF = cUF,
                        Uf = uf,
                        Name = ((string?)stEl.Attribute("name") ?? uf).Trim(),
                        Enabled = ParseBool((string?)stEl.Attribute("enabled"), true)
                    });
                }

                providers[key] = new NfeProviderConfigEntry
                {
                    Key = key,
                    Name = ((string?)el.Attribute("name") ?? key).Trim(),
                    Version = ((string?)el.Attribute("version") ?? "4.00").Trim(),
                    Enabled = ParseBool((string?)el.Attribute("enabled"), true),
                    Production = ReadEndpoints(el.Element("Production")),
                    Homologation = ReadEndpoints(el.Element("Homologation")),
                    States = states
                };
            }

            if (providers.Count == 0)
                return CreateFallbackSnapshot(filePath);

            return new NfeProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = defaultProvider,
                NationalEnvironment = nationalEnv,
                Providers = providers
            };
        }

        private static NfeServiceEndpoints ReadEndpoints(XElement? container)
        {
            return new NfeServiceEndpoints
            {
                NFeAutorizacao = ReadElement(container, "NFeAutorizacao"),
                NFeRetAutorizacao = ReadElement(container, "NFeRetAutorizacao"),
                NFeRecepcaoEvento = ReadElement(container, "NFeRecepcaoEvento"),
                NFeConsultaProtocolo = ReadElement(container, "NFeConsultaProtocolo"),
                NFeStatusServico = ReadElement(container, "NFeStatusServico"),
                NFeInutilizacao = ReadElement(container, "NFeInutilizacao")
            };
        }

        private static string ReadElement(XElement? container, string name, string fallback = "")
        {
            var val = ((string?)container?.Element(name) ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(val) ? fallback : val;
        }

        private static NfeProvidersSnapshot CreateFallbackSnapshot(string filePath)
        {
            var providers = new Dictionary<string, NfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["SVRS"] = new NfeProviderConfigEntry
                {
                    Key = "SVRS",
                    Name = "SEFAZ Virtual Rio Grande do Sul (SVRS)",
                    Version = "4.00",
                    Enabled = true,
                    Production = new NfeServiceEndpoints
                    {
                        NFeAutorizacao = "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
                        NFeRetAutorizacao = "https://nfe.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx",
                        NFeRecepcaoEvento = "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx",
                        NFeConsultaProtocolo = "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
                        NFeStatusServico = "https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx",
                        NFeInutilizacao = "https://nfe.svrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx"
                    },
                    Homologation = new NfeServiceEndpoints
                    {
                        NFeAutorizacao = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
                        NFeRetAutorizacao = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx",
                        NFeRecepcaoEvento = "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx",
                        NFeConsultaProtocolo = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
                        NFeStatusServico = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx",
                        NFeInutilizacao = "https://nfe-homologacao.svrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx"
                    }
                }
            };

            return new NfeProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = "SVRS",
                NationalEnvironment = new NfeNationalEnvironmentEndpoints(),
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

