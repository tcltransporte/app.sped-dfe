using DFe.Classes.Flags;
using System;
using System.Collections.Generic;


namespace services.Controllers.DFe.MDFe.Schemas
{

    // =========================
    // OBJETO RAIZ
    // =========================
    public class NFeRequestDto
    {
        //public InfNFeDto? infNFe { get; set; }
        public TipoAmbiente? tpAmb { get; set; }
        public List<Nfe>? nfes { get; set; }
    }

    public class Nfe
    {
        public string? xml { get; set; }
    }

    // =========================
    // INFNFE
    // =========================
    public class InfNFeDto
    {
        public IdeDto? ide { get; set; }
        public EmitDto? emit { get; set; }
        public DestDto? dest { get; set; }
        public List<DetDto>? det { get; set; }
        public TotalDto? total { get; set; }
        public TranspDto? transp { get; set; }
        public PagDto? pag { get; set; }
        public InfAdicDto? infAdic { get; set; }
    }

    // =========================
    // IDE
    // =========================
    public class IdeDto
    {
        public int? cUF { get; set; }
        public int? cNF { get; set; }
        public string? natOp { get; set; }
        public int? mod { get; set; }
        public int? serie { get; set; }
        public int? nNF { get; set; }
        public DateTime? dhEmi { get; set; }
        public int? tpNF { get; set; }
        public int? idDest { get; set; }
        public int? cMunFG { get; set; }
        public int? tpImp { get; set; }
        public int? tpEmis { get; set; }
        public int? tpAmb { get; set; }
        public int? finNFe { get; set; }
        public int? indFinal { get; set; }
        public int? indPres { get; set; }
        public int? procEmi { get; set; }
        public string? verProc { get; set; }
    }

    // =========================
    // EMITENTE
    // =========================
    public class EmitDto
    {
        public string? cnpj { get; set; }
        public string? xNome { get; set; }
        public string? xFant { get; set; }
        public EnderEmitDto? enderEmit { get; set; }
        public string? ie { get; set; }
        public int? crt { get; set; }
    }

    public class EnderEmitDto
    {
        public string? xLgr { get; set; }
        public string? nro { get; set; }
        public string? xBairro { get; set; }
        public int? cMun { get; set; }
        public string? xMun { get; set; }
        public int? uf { get; set; }
        public string? cep { get; set; }
        public int? cPais { get; set; }
        public string? xPais { get; set; }
        public string? fone { get; set; }
    }

    // =========================
    // DESTINATÁRIO
    // =========================
    public class DestDto
    {
        public string? cnpj { get; set; }
        public string? cpf { get; set; }
        public string? xNome { get; set; }
        public EnderDestDto? enderDest { get; set; }
        public int? indIEDest { get; set; }
        public string? ie { get; set; }
        public string? email { get; set; }
    }

    public class EnderDestDto
    {
        public string? xLgr { get; set; }
        public string? nro { get; set; }
        public string? xBairro { get; set; }
        public int? cMun { get; set; }
        public string? xMun { get; set; }
        public int? uf { get; set; }
        public string? cep { get; set; }
        public int? cPais { get; set; }
        public string? xPais { get; set; }
    }

    // =========================
    // PRODUTOS
    // =========================
    public class DetDto
    {
        public int? nItem { get; set; }
        public ProdDto? prod { get; set; }
        public ImpostoDto? imposto { get; set; }
    }

    public class ProdDto
    {
        public string? cProd { get; set; }
        public string? xProd { get; set; }
        public string? ncm { get; set; }
        public string? cfop { get; set; }
        public string? uCom { get; set; }
        public decimal? qCom { get; set; }
        public decimal? vUnCom { get; set; }
        public decimal? vProd { get; set; }
    }

    // =========================
    // IMPOSTOS
    // =========================
    public class ImpostoDto
    {
        public IcmsDto? icms { get; set; }
        public IbsCbsDto? ibscbs { get; set; }
    }

    public class IcmsDto
    {
        public int? csosn { get; set; }
    }

    // =========================
    // IBS / CBS - REFORMA
    // =========================
    public class IbsCbsDto
    {
        public int? cst { get; set; }
        public string? cClassTrib { get; set; }
        public IbsCbsGrupoDto? gIBSCBS { get; set; }
    }

    public class IbsCbsGrupoDto
    {
        public decimal? vBC { get; set; }
        public IbsUfDto? gIBSUF { get; set; }
        public IbsMunDto? gIBSMun { get; set; }
        public decimal? vIBS { get; set; }
        public CbsDto? gCBS { get; set; }
    }

    public class IbsUfDto
    {
        public decimal? pIBSUF { get; set; }
        public decimal? vIBSUF { get; set; }
    }

    public class IbsMunDto
    {
        public decimal? pIBSMun { get; set; }
        public decimal? vIBSMun { get; set; }
    }

    public class CbsDto
    {
        public decimal? pCBS { get; set; }
        public decimal? vCBS { get; set; }
    }

    // =========================
    // TOTAIS
    // =========================
    public class TotalDto
    {
        public IcmsTotDto? icmsTot { get; set; }
        public IbsCbsTotDto? ibscbsTot { get; set; }
    }

    public class IcmsTotDto
    {
        public decimal? vProd { get; set; }
        public decimal? vNF { get; set; }
    }

    public class IbsCbsTotDto
    {
        public decimal? vBCIBSCBS { get; set; }
        public decimal? vIBS { get; set; }
        public decimal? vCBS { get; set; }
    }

    // =========================
    // TRANSPORTE
    // =========================
    public class TranspDto
    {
        public int? modFrete { get; set; }
    }

    // =========================
    // PAGAMENTO
    // =========================
    public class PagDto
    {
        public List<DetPagDto>? detPag { get; set; }
    }

    public class DetPagDto
    {
        public int? tPag { get; set; }
        public decimal? vPag { get; set; }
    }

    // =========================
    // INFORMAÇÕES ADICIONAIS
    // =========================
    public class InfAdicDto
    {
        public string? infCpl { get; set; }
    }

}