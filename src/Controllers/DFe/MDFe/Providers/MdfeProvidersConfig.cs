using System.Xml.Linq;

namespace application.services.Controllers.DFe.MDFe.Providers
{
    public sealed class MdfeStateEntry
    {
        public string CUF { get; init; } = string.Empty;
        public string Uf { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
    }

    public sealed class MdfeServiceEndpoints
    {
        public string MDFeRecepcaoSinc { get; init; } = string.Empty;
        public string MDFeRecepcaoEvento { get; init; } = string.Empty;
        public string MDFeConsulta { get; init; } = string.Empty;
        public string MDFeStatusServico { get; init; } = string.Empty;
        public string MDFeConsNaoEnc { get; init; } = string.Empty;
        public string QrCode { get; init; } = string.Empty;
    }

    public sealed class MdfeProviderConfigEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = "3.00";
        public bool Enabled { get; init; } = true;
        public MdfeServiceEndpoints Production { get; init; } = new();
        public MdfeServiceEndpoints Homologation { get; init; } = new();
        public IReadOnlyList<MdfeStateEntry> States { get; init; } = Array.Empty<MdfeStateEntry>();
    }

    public sealed class MdfeProvidersSnapshot
    {
        public string ConfigFilePath { get; init; } = string.Empty;
        public string DefaultProvider { get; init; } = "SVRS";
        public IReadOnlyDictionary<string, MdfeProviderConfigEntry> Providers { get; init; } =
            new Dictionary<string, MdfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);

        public MdfeProviderConfigEntry? GetProvider(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return Providers.TryGetValue(key.Trim(), out var entry) && entry.Enabled ? entry : null;
        }

        public MdfeProviderConfigEntry ResolveProvider(string? ufOrCuf, string? requestedProviderKey = null)
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
                ?? throw new InvalidOperationException($"Nenhum provedor SEFAZ de MDF-e configurado para a UF '{ufOrCuf}'.");
        }

        public MdfeServiceEndpoints ResolveEndpoints(string? ufOrCuf, bool useHomologation, string? requestedProviderKey = null)
        {
            var provider = ResolveProvider(ufOrCuf, requestedProviderKey);
            return useHomologation ? provider.Homologation : provider.Production;
        }
    }

    public sealed class MdfeProvidersXmlStore
    {
        private readonly object syncLock = new();
        private string? cachedPath;
        private DateTime? cachedLastWriteUtc;
        private MdfeProvidersSnapshot? cachedSnapshot;

        public MdfeProvidersSnapshot GetConfig()
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
                Path.Combine(AppContext.BaseDirectory, "Config", "mdfe-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Config", "mdfe-providers.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Config", "mdfe-providers.xml"),
                Path.Combine(AppContext.BaseDirectory, "src", "Config", "mdfe-providers.xml"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static MdfeProvidersSnapshot LoadFromFile(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null)
                return CreateFallbackSnapshot(filePath);

            var defaultProvider = root.Element("DefaultProvider")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(defaultProvider))
                defaultProvider = "SVRS";

            var providers = new Dictionary<string, MdfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase);
            var providersEl = root.Element("Providers");
            if (providersEl != null)
            {
                foreach (var pEl in providersEl.Elements("Provider"))
                {
                    var key = pEl.Attribute("key")?.Value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var name = pEl.Attribute("name")?.Value?.Trim() ?? key;
                    var version = pEl.Attribute("version")?.Value?.Trim() ?? "3.00";
                    var enabled = bool.TryParse(pEl.Attribute("enabled")?.Value?.Trim(), out var en) ? en : true;

                    var prodEl = pEl.Element("Production");
                    var homEl = pEl.Element("Homologation");

                    var prod = new MdfeServiceEndpoints
                    {
                        MDFeRecepcaoSinc = ReadElement(prodEl, "MDFeRecepcaoSinc"),
                        MDFeRecepcaoEvento = ReadElement(prodEl, "MDFeRecepcaoEvento"),
                        MDFeConsulta = ReadElement(prodEl, "MDFeConsulta"),
                        MDFeStatusServico = ReadElement(prodEl, "MDFeStatusServico"),
                        MDFeConsNaoEnc = ReadElement(prodEl, "MDFeConsNaoEnc"),
                        QrCode = ReadElement(prodEl, "QrCode")
                    };

                    var hom = new MdfeServiceEndpoints
                    {
                        MDFeRecepcaoSinc = ReadElement(homEl, "MDFeRecepcaoSinc"),
                        MDFeRecepcaoEvento = ReadElement(homEl, "MDFeRecepcaoEvento"),
                        MDFeConsulta = ReadElement(homEl, "MDFeConsulta"),
                        MDFeStatusServico = ReadElement(homEl, "MDFeStatusServico"),
                        MDFeConsNaoEnc = ReadElement(homEl, "MDFeConsNaoEnc"),
                        QrCode = ReadElement(homEl, "QrCode")
                    };

                    var states = new List<MdfeStateEntry>();
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
                                states.Add(new MdfeStateEntry
                                {
                                    CUF = cUF,
                                    Uf = uf,
                                    Name = sName,
                                    Enabled = sEnabled
                                });
                            }
                        }
                    }

                    providers[key] = new MdfeProviderConfigEntry
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

            return new MdfeProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = defaultProvider,
                Providers = providers
            };
        }

        private static string ReadElement(XElement? parent, string elementName, string defaultValue = "")
        {
            if (parent == null) return defaultValue;
            var val = parent.Element(elementName)?.Value?.Trim();
            return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
        }

        private static MdfeProvidersSnapshot CreateFallbackSnapshot(string filePath)
        {
            var svrs = new MdfeProviderConfigEntry
            {
                Key = "SVRS",
                Name = "Sefaz Virtual RS",
                Version = "3.00",
                Enabled = true,
                Production = new MdfeServiceEndpoints
                {
                    MDFeRecepcaoSinc = "https://mdfe.svrs.rs.gov.br/ws/MDFeRecepcaoSinc/MDFeRecepcaoSinc.asmx",
                    MDFeRecepcaoEvento = "https://mdfe.svrs.rs.gov.br/ws/MDFeRecepcaoEvento/MDFeRecepcaoEvento.asmx",
                    MDFeConsulta = "https://mdfe.svrs.rs.gov.br/ws/MDFeConsulta/MDFeConsulta.asmx",
                    MDFeStatusServico = "https://mdfe.svrs.rs.gov.br/ws/MDFeStatusServico/MDFeStatusServico.asmx",
                    MDFeConsNaoEnc = "https://mdfe.svrs.rs.gov.br/ws/MDFeConsNaoEnc/MDFeConsNaoEnc.asmx",
                    QrCode = "https://dfe-portal.svrs.rs.gov.br/mdfe/qrCode"
                },
                Homologation = new MdfeServiceEndpoints
                {
                    MDFeRecepcaoSinc = "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeRecepcaoSinc/MDFeRecepcaoSinc.asmx",
                    MDFeRecepcaoEvento = "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeRecepcaoEvento/MDFeRecepcaoEvento.asmx",
                    MDFeConsulta = "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeConsulta/MDFeConsulta.asmx",
                    MDFeStatusServico = "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeStatusServico/MDFeStatusServico.asmx",
                    MDFeConsNaoEnc = "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeConsNaoEnc/MDFeConsNaoEnc.asmx",
                    QrCode = "https://dfe-portal.svrs.rs.gov.br/mdfe/qrCode"
                }
            };

            return new MdfeProvidersSnapshot
            {
                ConfigFilePath = filePath,
                DefaultProvider = "SVRS",
                Providers = new Dictionary<string, MdfeProviderConfigEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SVRS"] = svrs
                }
            };
        }
    }
}
