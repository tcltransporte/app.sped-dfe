# NFS-e (Multi-Provider)

A API de NFS-e recebe um **único layout XML comum** para todos os municípios e provedores (`NACIONAL`, `ISSNET`, etc.).
Cada provider transforma esse XML comum para o seu respectivo formato, calcula os identificadores (`Id`), realiza a assinatura digital (`XMLDSig`) e transmite utilizando o protocolo exigido pelo provedor (REST GZip+Base64 ou SOAP).

## Exemplos
- **Emissão (`POST /DFe/nfse/generate`):** [emissao-exemplo.xml](./emissao-exemplo.xml)
- **Cancelamento (`POST /DFe/nfse/cancel`):** [cancelamento-exemplo.xml](./cancelamento-exemplo.xml)

## Resolução Automática
1. **Município (Provider):** definido em `src/Config/nfse-providers.xml` a partir de `<cLocEmi>` (na emissão) ou dos 7 primeiros dígitos de `<chNFSe>` (no cancelamento):
   - `5108402` (Várzea Grande - MT) -> Provider `NACIONAL` (Emissor Nacional SEFIN/ADN)
   - `5208707` (Goiânia - GO) -> Provider `ISSNET`
2. **Ambiente (`<tpAmb>`):**
   - `1` = Produção
   - `2` = Homologação / Produção Restrita

