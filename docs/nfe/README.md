# NF-e (Multi-Provider por SEFAZ Estadual)

A API de NF-e resolve automaticamente os endpoints da SEFAZ Estadual ou Sefaz Virtual (`SVRS` / `SVAN`) a partir da configuração em `src/Config/nfe-providers.xml`, utilizando `<cUF>` e `<tpAmb>` (`1` = Produção, `2` = Homologação).

## Exemplos
- **Emissão (`POST /DFe/nfe/generate`):** [emissao-exemplo.xml](./emissao-exemplo.xml)
- **Distribuição DF-e (`POST /DFe/nfe/distribuition`):** [distribuicao-exemplo.xml](./distribuicao-exemplo.xml)
- **Manifestação do Destinatário (`POST /DFe/nfe/manifestation`):** [manifestacao-exemplo.xml](./manifestacao-exemplo.xml)
- **Cancelamento (`POST /DFe/nfe/cancel`):** payload JSON `{ "chNFe": "...", "nProt": "...", "xJust": "...", "cnpj": "...", "tpAmb": 1 }`

