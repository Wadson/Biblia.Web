# Atualizações automáticas

O aplicativo consulta o manifesto publicado em:

`https://raw.githubusercontent.com/Wadson/Biblia.Web/main/version.json`

Publique o arquivo `version.json` na raiz do repositório. A URL do instalador deve ser direta, usar HTTPS e terminar em `.exe` — não use a sintaxe de link Markdown dentro do JSON.

```json
{
  "versao": "1.0.3",
  "url": "https://github.com/Wadson/Biblia.Web/releases/download/v1.0.3/BibliaTema_1.0.3_Instalador.exe",
  "descricao": "Correções e melhorias da versão 1.0.3"
}
```

Para publicar uma versão:

1. Atualize `Version`, `AssemblyVersion`, `FileVersion` e `InformationalVersion` no projeto para a nova versão.
2. Gere e envie o instalador como um Release do GitHub.
3. Atualize o `version.json` com a nova versão e o link direto do arquivo do Release.
4. Ao abrir o aplicativo, o aviso de atualização aparecerá. O usuário baixa o instalador em **Configurações** e confirma a instalação.
