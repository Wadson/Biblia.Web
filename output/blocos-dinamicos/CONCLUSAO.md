# Blocos de texto dinâmicos — correção concluída em 07/09/2026

A implementação anterior já persistia blocos e os incluía no PDF, mas deixava a ação abaixo da pesquisa/listagem, e a configuração de ordem perdia o contexto do tema. Esta revisão torna o fluxo acessível na tela usada para vincular versículos.

## Alterações

- `Biblia.Web.Web/Components/Pages/ThemeVerseLinker.razor`: ação Adicionar bloco de texto junto de Salvar vinculação; atalho para a sequência; instrução quando nenhum tema está selecionado; suporte ao tema na URL; editor com identidade isolada por tema.
- `Biblia.Web.Web/Components/Shared/ThemeContentEditor.razor`: abertura com foco/rolagem para o texto; atualização durante digitação; prévia estruturada, contador de caracteres, mensagem após salvar; alternância manual/canônica no próprio editor, com confirmação para voltar ao canônico.
- `Biblia.Web.Web/Components/Pages/Themes.razor`: ação Versículos e blocos em cada tema, abrindo o tema já selecionado.
- `Biblia.Web.Web/Components/Pages/ThemeOrdering.razor`: retorno à vinculação preservando o tema informado.

Não houve nova migração: permanece o schema 5, a tabela única ThemeContent, posições inteiras e operações transacionais. Textos e propriedades são tipados, sem HTML livre. Permanecem texto multilinha, cores, fonte, marcadores, negrito/itálico, editar, excluir com confirmação e setas. A ordenação manual intercala referências e blocos; no retorno ao canônico os blocos mantêm seus espaços e somente os versículos são reordenados. Observações e versões dos vínculos são preservadas. Backup formato 2 e compatibilidade antiga permanecem conforme o relatório anterior em `output/recursos-integrados/RELATORIO_FINAL.md`.

## Verificação

Restore aprovado. Build completo Release aprovado, zero avisos e erros na execução final. 144 testes Debug e 144 testes Release aprovados. Uma tentativa de build coincidiu com os testes e falhou por DLL bloqueada; a compilação foi repetida sequencialmente e passou.

Interface real em base isolada: abrir por Temas → Versículos e blocos; tema selecionado; botão superior abre editor e foca textarea; digitação atualiza prévia numerada; salvar exibe sucesso; ativar manual sem sair; mover bloco até ficar entre Gênesis e Apocalipse; recarregar preserva sequência; editar itálico preserva posição; ir à configuração e voltar preserva tema; gerar PDF pela interface publicada. As duas páginas foram renderizadas e conferidas, incluindo posição, numeração, itálico, observações e rodapés. Logs da instância de QA sem erros.

Evidências nesta pasta: `blocos-debug.trx`, `blocos-release.trx`, `bloco-intercalado.pdf`, `bloco-intercalado.png`, `bloco-intercalado-pagina2.png`, logs e `interface-antes.zip`.

## Publicação e instalação

Publicação final em `D:\Publicacao\Biblia.Web`, somente o executável Web; AppHost não publicado. A cópia instalada em `C:\Program Files\Biblia.Web` foi atualizada com autorização administrativa do Windows: sete arquivos alterados, com conferência de hash. Configurações, bancos e diretórios de dados não foram substituídos. Backup dos arquivos anteriores em `instalacao-anterior`; resultado em `atualizacao-instalacao.txt`.

SHA-256 Web, igual na publicação e instalação: `67574EAC0F8E81A154688F527938832563A7B5E82E461B24E879D7ADFEB57545`.

A reabertura pelo ambiente do Codex recebeu redirecionamento de AppData do Windows para uma base antiga de QA do pacote Codex. Essa instância foi encerrada para não confundir com os dados habituais. A base pessoal foi consultada em modo somente leitura e continua com os 22 temas. O aplicativo atualizado deve ser aberto pelo atalho habitual do Windows.

Não foram adicionados dados de teste à base pessoal. As verificações de edição e PDF usaram `D:\Projetos\Biblia.Web\tmp\integrated-published-final`. Não há garantia de cobertura de todas as combinações possíveis; nesta revisão não se repetiram todas as variações de navegador/carga ou falhas físicas de disco.
