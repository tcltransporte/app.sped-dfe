# MDF-e (Multi-Provider por SVRS / SEFAZ Estadual)

A API de MDF-e resolve automaticamente os endpoints da SEFAZ Autorizadora (por padrão `SVRS` ou `SEFAZ_SP`) a partir da configuração em `src/Config/mdfe-providers.xml`, utilizando `<cUF>` e `<tpAmb>` (`1` = Produção, `2` = Homologação).

A transmissão utiliza o padrão nacional **MDF-e 3.00** síncrono com compactação GZip Base64 e assinatura digital RSA-SHA1 (XMLDSig) automática, sem necessidade de bibliotecas legadas.

## Endpoints

### 1. Listagem de Provedores
- **Método:** `GET /DFe/mdfe/providers`
- Retorna o JSON com todos os autorizadores configurados, endpoints de produção/homologação e mapeamento de UFs.

### 2. Emissão Síncrona
- **Método:** `POST /DFe/mdfe/generate`
- **Headers:**
  - `X-Cert-Base64`: Certificado digital modelo A1 em Base64
  - `X-Cert-Password`: Senha do certificado
  - `X-MDFe-Provider` *(opcional)*: Força um provedor específico (ex.: `SVRS`, `SEFAZ_SP`)
  - `Content-Type`: `application/xml`
- **Body:** XML do `<MDFe>` no leiaute v3.00 (ver [emissao-exemplo.xml](./emissao-exemplo.xml))
- **Retorno:** XML do `<mdfeProc>` completo com o MDF-e assinado e o protocolo de autorização (`protMDFe`).

### 3. Cancelamento de MDF-e
- **Método:** `POST /DFe/mdfe/cancel`
- **Headers:** `X-Cert-Base64`, `X-Cert-Password`, `X-MDFe-Provider` *(opcional)*
- **Body JSON:**
```json
{
  "chMDFe": "52261104058687000177580010000001001123456780",
  "nProt": "152260000123456",
  "xJust": "Cancelamento por motivo de desistência da viagem de transporte",
  "cnpj": "04058687000177",
  "tpAmb": 2,
  "cUF": 52,
  "nSeqEvento": 1
}
```
- **Retorno:** XML do `<retEventoMDFe>` retornado pela SEFAZ.

### 4. Encerramento de MDF-e
- **Método:** `POST /DFe/mdfe/close`
- **Headers:** `X-Cert-Base64`, `X-Cert-Password`, `X-MDFe-Provider` *(opcional)*
- **Body JSON:**
```json
{
  "chMDFe": "52261104058687000177580010000001001123456780",
  "nProt": "152260000123456",
  "dtEnc": "2026-11-18T10:00:00-03:00",
  "cUFEncerramento": 51,
  "cMunEncerramento": 5103403,
  "cnpj": "04058687000177",
  "tpAmb": 2,
  "cUF": 52,
  "nSeqEvento": 1
}
```
- **Retorno:** XML do `<retEventoMDFe>` de encerramento retornado pela SEFAZ.

### 5. Consulta de MDF-e Não Encerrados
- **Método:** `POST /DFe/mdfe/nonclosed`
- **Headers:** `X-Cert-Base64`, `X-Cert-Password`
- **Body JSON:**
```json
{
  "cnpj": "04058687000177",
  "cUF": 52,
  "tpAmb": 2
}
```
- **Retorno:** XML do `<retConsMDFeNaoEnc>` retornado pela SEFAZ com a relação de manifestos pendentes de encerramento.
