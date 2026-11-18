# CT-e (Multi-Provider por SEFAZ Estadual / SVRS)

A API de CT-e resolve automaticamente os endpoints da SEFAZ Estadual ou Sefaz Virtual (`SVRS` / `SEFAZ_SP` / `SEFAZ_MT` / `SEFAZ_MS` / `SEFAZ_MG` / `SEFAZ_PR`) a partir da configuração em `src/Config/cte-providers.xml`, utilizando `<cUF>` e `<tpAmb>` (`1` = Produção, `2` = Homologação).

A transmissão utiliza o padrão nacional **CT-e 4.00** síncrono com compactação GZip Base64 e assinatura digital RSA-SHA1 (XMLDSig) automática, sem necessidade de bibliotecas legadas.

## Endpoints

### 1. Listagem de Provedores
- **Método:** `GET /DFe/cte/providers`
- Retorna o JSON com todos os autorizadores configurados, endpoints de produção/homologação e mapeamento de UFs.

### 2. Emissão Síncrona
- **Método:** `POST /DFe/cte/generate`
- **Headers:**
  - `X-Cert-Base64`: Certificado digital modelo A1 em Base64
  - `X-Cert-Password`: Senha do certificado
  - `X-CTe-Provider` *(opcional)*: Força um provedor específico (ex.: `SVRS`, `SEFAZ_SP`, `SEFAZ_MT`)
  - `Content-Type`: `application/xml`
- **Body:** XML do `<CTe>` no leiaute v4.00 (ver [emissao-exemplo.xml](./emissao-exemplo.xml))
- **Retorno:** XML do `<cteProc>` completo com o CT-e assinado e o protocolo de autorização (`protCTe`).

### 3. Cancelamento de CT-e
- **Método:** `POST /DFe/cte/cancel`
- **Headers:** `X-Cert-Base64`, `X-Cert-Password`, `X-CTe-Provider` *(opcional)*
- **Body JSON:**
```json
{
  "chCTe": "52261104058687000177570010000001001123456780",
  "nProt": "152260000123456",
  "xJust": "Cancelamento por motivo de erro no preenchimento de valores",
  "cnpj": "04058687000177",
  "tpAmb": 2,
  "cUF": 52,
  "nSeqEvento": 1
}
```
- **Retorno:** XML do `<retEventoCTe>` retornado pela SEFAZ.

### 4. Distribuição DF-e de CT-e (Ambiente Nacional)
- **Método:** `POST /DFe/cte/distribuition`
- **Headers:** `X-Cert-Base64`, `X-Cert-Password`
- **Body XML:** ver [distribuicao-exemplo.xml](./distribuicao-exemplo.xml)
- **Retorno:** XML do `<retDistDFeInt>` retornado pelo Ambiente Nacional do CT-e.
