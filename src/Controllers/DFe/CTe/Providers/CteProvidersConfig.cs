using System.Xml.Linq;

namespace application.services.Controllers.DFe.CTe.Providers
{
    public sealed class CteStateEntry
    {
        public string CUF { get; init; } = string.Empty;
        public string Uf { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
    }

    public sealed class CteServiceEndpoints
    {
        public string CTeRecepcaoSinc { get; init; } = string.Empty;
        public string CTeRecepcaoEvento { get; init; } = string.Empty;
        public string CTeConsultaProtocolo { get; init; } = string.Empty;
        public string CTeStatusServico { get; init; } = string.Empty;
        public string QrCode { get; init; } = string.Empty;
    }

    public sealed class CteNationalEnvironmentEndpoints
    {
        public string DistribuicaoDFeProduction { get; init; } = "https://www1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx";
        public string DistribuicaoDFeHomologation { get; init; } = "https://hom1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx";
    }

    public sealed class CteProviderConfigEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = "4.00";
        public bool Enabled { get; init; } = true;
        public CteServiceEndpoints Production { get; init; } = new();
        public CteServiceEndpoints Homologation { get; init; } = new();
        public IReadOnlyList<CteStateEntry> States { get; init; } = Array.Empty<CteStateEntry>();
    }

    public sealed class CteProvidersSnapshot
    {
        public string ConfigFilePath { get; init; } = string.Empty;
        public string DefaultProvider { get; init; } = "SVRS";
        public CteNationalEnvironmentEndpoints NationalEnvironment { get; init; } = new();
        public IReadOnlyDictionary<string, CteProviderConfigEntry> Providers { get; init; } =
            new Dictionary<string, CteProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);

        public CteProviderConfigEntry? GetProvider(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return Providers.TryGetValue(key.Trim(), out var entry) && entry.Enabled ? entry : null;
        }

        public CteProviderConfigEntry ResolveProvider(string? ufOrCuf, string? requestedProviderKey = null)
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
                ?? throw new InvalidOperationException($"Nenhum provedor SEFAZ de CT-e configurado para a UF '{ufOrCuf}'.");
        }

        public CteServiceEndpoints ResolveEndpoints(string? ufOrCuf, bool useHomologation, string? requestedProviderKey = null)
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
    }

    public sealed class CteProvidersXmlStore
    {
        private readonly object syncLock = new();
        private string? cachedPath;
        private DateTime? cachedLastWriteUtc;
        private CteProvidersSnapshot? cachedSnapshot;

        public CteProvidersSnapshot GetConfig()
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
                Path.Combine(AppContext.BaseDirectory, "Config", "cte-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Config", "cte-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Config", "cte-providers.xml"),
                Path.Combine(AppContext.BaseDirectory, "src", "Config", "cte-providers.xml"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static CteProvidersSnapshot LoadFromFile(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null)
                return CreateFallbackSnapshot(filePath);

            var defaultProvider = root.Element("DefaultProvider")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(defaultProvider))
                defaultProvider = "SVRS";

            var natEl = root.Element("NationalEnvironment");
            var nationalEnv = new CteNationalEnvironmentEndpoints
            {
                DistribuicaoDFeProduction = ReadElement(natEl, "DistribuicaoDFeProduction", "https://www1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx"),
                DistribuicaoDFeHomologation = ReadElement(natEl, "DistribuicaoDFeHomologation", "https://hom1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx")
            };

            var providers = new Dictionary<string, CteProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);
            var providersEl = root.Element("Providers");
            if (providersEl != null)
            {
                foreach (var pEl in providersEl.Elements("Provider"))
                {
                    var key = pEl.Attribute("key")?.Value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var name = pEl.Attribute("name")?.Value?.Trim() ?? key;
                    var version = pEl.Attribute("version")?.Value?.Trim() ?? "4.00";
                    var enabled = bool.TryParse(pEl.Attribute("enabled")?.Value?.Trim(), out var en) ? en : true;

                    var prodEl = pEl.Element("Production");
                    var homEl = pEl.Element("Homologation");

                    var prod = new CteServiceEndpoints
                    {
                        CTeRecepcaoSinc = ReadElement(prodEl, "CTeRecepcaoSinc"),
                        CTeRecepcaoEvento = ReadElement(prodEl, "CTeRecepcaoEvento"),
                        CTeConsultaProtocolo = ReadElement(prodEl, "CTeConsultaProtocolo"),
                        CTeStatusServico = ReadElement(prodEl, "CTeStatusServico"),
                        QrCode = ReadElement(prodEl, "QrCode")
                    };

                    var hom = new CteServiceEndpoints
                    {
                        CTeRecepcaoSinc = ReadElement(homEl, "CTeRecepcaoSinc"),
                        CTeRecepcaoEvento = ReadElement(homEl, "CTeRecepcaoEvento"),
                        CTeConsultaProtocolo = ReadElement(homEl, "CTeConsultaProtocolo"),
                        CTeStatusServico = ReadElement(homEl, "CTeStatusServico"),
                        QrCode = ReadElement(homEl, "QrCode")
                    };

                    var states = new List<CteStateEntry>();
                    var statesEl = pEl.Element("States");
                    if (statesEl != null)
                    {
                        foreach (var sEl in statesEl.Elements("State"))
                        {
                            var cUF = sEl.Attribute("cUF")?.Value?.Trim() ?? string.Empty;
                            var uf = sEl.Attribute("uf")?.Value?.Trim() ?? string.Empty;
                            var sName = sEl.Attribute("name")?.Value?.Trim() ?? uf;
                            var sEnabled = bool.TryParse(sEl.Attribute("enabled")?.Value?.Trim(), out var sEn) ? sEn : true;

                            if (!string.IsNullOrWhiteSpace(cUF) || !string.IsNullOrWhiteSpace(uf))
                            {
                                states.Add(new CteStateEntry
                                {
                                    CUF = cUF,
                                    Uf = uf,
                                    Name = sName,
                                    Enabled = sEnabled
                                });
                            }
                        }
                    }

                    providers[key] = new CteProviderConfigEntry
                    {
                        Key = key,
                        Name = name,
                        Version = version,
                        Enabled = enabled,
                        Production = prod,
                        Homologation = hom,
                        States = states
                    };
                }
            }

            return new CteProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = defaultProvider,
                NationalEnvironment = nationalEnv,
                Providers = providers
            };
        }

        private static string ReadElement(XElement? parent, string elementName, string defaultValue = "")
        {
            if (parent == null) return defaultValue;
            var val = parent.Element(elementName)?.Value?.Trim();
            return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
        }

        private static CteProvidersSnapshot CreateFallbackSnapshot(string filePath)
        {
            var svrs = new CteProviderConfigEntry
            {
                Key = "SVRS",
                Name = "Sefaz Virtual RS",
                Version = "4.00",
                Enabled = true,
                Production = new CteServiceEndpoints
                {
                    CTeRecepcaoSinc = "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx",
                    CTeRecepcaoEvento = "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx",
                    CTeConsultaProtocolo = "https://cte.svrs.rs.gov.br/ws/CTeConsultaV4/CTeConsultaV4.asmx",
                    CTeStatusServico = "https://cte.svrs.rs.gov.br/ws/CTeStatusServicoV4/CTeStatusServicoV4.asmx",
                    QrCode = "https://dfe-portal.svrs.rs.gov.br/cte/qrCode"
                },
                Homologation = new CteServiceEndpoints
                {
                    CTeRecepcaoSinc = "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx",
                    CTeRecepcaoEvento = "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx",
                    CTeConsultaProtocolo = "https://cte-homologacao.svrs.rs.gov.br/ws/CTeConsultaV4/CTeConsultaV4.asmx",
                    CTeStatusServico = "https://cte-homologacao.svrs.rs.gov.br/ws/CTeStatusServicoV4/CTeStatusServicoV4.asmx",
                    QrCode = "https://dfe-portal.svrs.rs.gov.br/cte/qrCode"
                }
            };

            return new CteProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = "SVRS",
                NationalEnvironment = new CteNationalEnvironmentEndpoints(),
                Providers = new Dictionary<string, CteProviderConfigEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SVRS"] = svrs
                }
            };
        }
    }
}
