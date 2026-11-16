namespace application.services.Controllers.DFe.NFSe.Providers
{
    public sealed class NFSeProviderContext
    {
        public string RootLocalName { get; init; } = string.Empty;
        public string? RequestedProviderKey { get; init; }
        public bool IsDpsXml { get; init; }
        public string? MunicipioPrestacao { get; init; }
        public string? MunicipioIncidencia { get; init; }
        public string? MunicipioPrestador { get; init; }
        public string? MunicipioTomador { get; init; }

        public string? CodigoMunicipio =>
            MunicipioPrestador ??
            MunicipioPrestacao ??
            MunicipioIncidencia ??
            MunicipioTomador;

        public IReadOnlyList<string> CandidateMunicipios =>
            new[] { MunicipioPrestador, MunicipioPrestacao, MunicipioIncidencia, MunicipioTomador }
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
