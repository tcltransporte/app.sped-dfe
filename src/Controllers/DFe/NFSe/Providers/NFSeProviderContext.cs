namespace application.services.Controllers.DFe.NFSe.Providers
{
    public sealed class NFSeProviderContext
    {
        public string? MunicipioPrestacao { get; init; }
        public string? MunicipioIncidencia { get; init; }
        public string? MunicipioPrestador { get; init; }
        public string? MunicipioTomador { get; init; }

        public string? CodigoMunicipio =>
            MunicipioPrestacao ??
            MunicipioIncidencia ??
            MunicipioPrestador ??
            MunicipioTomador;
    }
}
