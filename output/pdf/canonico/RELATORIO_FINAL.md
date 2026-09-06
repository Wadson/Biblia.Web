# Ordenação canônica — conclusão em 06/09/2026

Implementação concluída e validada na publicação `D:\Publicacao\Biblia.Web`, executada em `http://127.0.0.1:5089`.

## Diagnóstico e contrato

A divergência confirmada estava na busca de Referências guardadas: `SavedReferenceRepository.SearchAsync` usava `ORDER BY UpdatedAt DESC`. Os caminhos principais de vinculação e relatório já ordenavam numericamente, mas não havia contrato central completo nem validação suficiente da correspondência entre ID e nome dos livros.

`BookReferenceId` continua sendo a posição canônica de 1 a 66, distinta do ID local da tabela de cada versão. Não foi criada coluna adicional. `BibleCanonicalOrder` centraliza catálogo imutável, aliases inequívocos, validação de identidade/testamento e a chave livro → capítulo → início → fim → ID. “Jo” ambíguo não é resolvido por suposição.

Foram auditados: consultas de livros e pesquisa bíblica; referências guardadas e consultas por temas; GetLinkedAsync; recarga da coleção visual após inclusão, edição e desvinculação; troca de versão; ReportService.BuildThemesAsync e preservação da sequência pelo PdfService. O layout e a ordem dos temas não foram alterados.

## Arquivos de implementação

| Arquivo (relativo à raiz do projeto) | Motivo |
| --- | --- |
| Biblia.Web.Core/Domain/Rules/BibleCanonicalOrder.cs | Catálogo de 66 livros, aliases, validação e chave numérica única. |
| Biblia.Web.Core/Infrastructure/BibleDatabases/BibleBookReader.cs | Leitura compartilhada dos livros para validação. |
| Biblia.Web.Core/Application/Interfaces/IBibleValidationService.cs | Contrato de validação canônica de instalações existentes. |
| Biblia.Web.Core/Infrastructure/BibleDatabases/BibleValidationService.cs | Rejeição de identidades, IDs e testamentos incompatíveis. |
| Biblia.Web.Core/Infrastructure/BibleDatabases/BibleRepository.cs | Garantia de identidade ao fornecer livros e consultas auditadas. |
| Biblia.Web.Core/Application/Services/InitialBibleInstallationService.cs | Revalidar versões instaladas mesmo quando o hash coincide com o manifesto; desabilitar incompatíveis. |
| Biblia.Web.Core/Application/Services/BookNameResolver.cs | Reutilizar catálogo central. |
| Biblia.Web.Core/Application/Services/SavedReferenceService.cs | Garantir ordem canônica na fronteira pública. |
| Biblia.Web.Core/Infrastructure/Repositories/SavedReferenceRepository.cs | Substituir ordenação por atualização pela chave canônica no SQL. |
| Biblia.Web.Core/Application/Services/ThemeVerseLinkService.cs | Usar comparador compartilhado e preservar fim do intervalo. |
| Biblia.Web.Core/Domain/Entities/ThemeVerseLinking.cs | Transportar fim do intervalo na referência visual. |
| Biblia.Web.Core/Application/Services/ReportService.cs | Ordenar seções pelo contrato compartilhado. |
| Biblia.Web.Web/Components/Pages/ThemeVerseLinker.razor | Ordenar coleção recarregada e manter identidade visual dos cards. |
| tools/ThemeReportQa/Program.cs | Modo de auditoria canônica dos bancos usando a DLL publicada. |

As três mensagens com acentuação corrompida encontradas na revisão foram corrigidas antes da publicação final. Alterações preexistentes de ícones, arquivos de IDE e outros artefatos não foram revertidas.

## Versões auditadas

ACF, ARA, ARC, AS21, KJF, NBV e NVI: sem problemas de identidade canônica. Foram verificadas 21 cópias: sete no projeto, sete instaladas em `%LOCALAPPDATA%\BibliaTema\Bibles` e sete na publicação. Os hashes coincidem entre as três localizações. Evidência: `D:\Projetos\Biblia.Web\tmp\canonical\audit-canonical.json`.

## Testes e compilação

- `dotnet restore`: sucesso.
- Build da solução em Debug: sucesso, zero avisos e erros; 102 testes aprovados no trabalho anterior à interrupção.
- Build da solução em Release: sucesso, zero avisos e erros.
- Testes Release finais: 102 aprovados, zero falhas e zero ignorados. Evidência: `D:\Projetos\Biblia.Web\tmp\canonical\test-results\canonical-release.trx`.
- `dotnet publish Biblia.Web.Web/Biblia.Web.Web.csproj -c Release --no-build --no-restore -o D:/Publicacao/Biblia.Web`: sucesso.
- Hashes das DLLs Core e Web publicadas conferidos contra os binários Release: iguais.

Testes novos: `CanonicalIdentityTests` (catálogo completo, aliases, IDs duplicados/ausentes/fora do intervalo, identidades trocadas e testamentos); `CanonicalThemeFlowTests` (66 livros em ordem inversa e aleatória, duas versões, recarga, edição, remoção, reinclusão, duplicidade de seleção, comparações numéricas, intervalos, desempate por ID, dois temas e extração do PDF); teste de 66 livros em `ThemeReportServiceTests`. Fixtures de importação, catálogo, repositório e validação foram adaptadas ao contrato. `InitialBibleInstallationServiceTests` cobre arquivo inalterado que coincide com manifesto e precisa ser desabilitado após reprovação canônica.

A suíte existente de PDF cobre fontes personalizadas, sumário/paginação, conteúdo extenso, observações, totais, cancelamento e geração concorrente.

## QA visual e publicação

O QA dos 66 livros usa dois temas, 138 referências no total, entrada inversa e casos numéricos adicionais de João. A sequência completa e observações são verificadas no texto extraído. As 23 páginas foram renderizadas e inspecionadas no trabalho original, conforme histórico e confirmação do usuário. Artefato: `D:\Projetos\Biblia.Web\output\pdf\canonico\qa-ordem-canonica-66-livros.pdf`.

Na publicação, o tema `QA Ordem Canonica 20260906` foi reaberto e apresentou Gênesis 1:1, Salmos 23:1, Mateus 5:3, João 3:16 e Apocalipse 21:4. A edição da observação de João preservou a posição. Mateus foi desvinculado e reincluído: a coleção voltou imediatamente à mesma ordem, sem duplicações. A troca de ACF para NVI preservou a sequência e a observação.

Foram gerados pela interface publicada dois PDFs, cada um com um tema e cinco referências. Ambos tiveram o texto extraído e a ordem e observação verificadas programaticamente. As duas páginas foram renderizadas e inspecionadas integralmente, sem cortes ou sobreposições:

- `D:\Projetos\Biblia.Web\output\pdf\canonico\qa-publicado-acf.pdf`
- `D:\Projetos\Biblia.Web\output\pdf\canonico\qa-publicado-nvi.pdf`

## Recuperação do banco local

Durante o QA publicado foi detectada corrupção de índices SQLite nas tabelas SavedReference/ReferenceTheme. As instâncias foram encerradas e os arquivos db/wal/shm preservados em `D:\Projetos\Biblia.Web\tmp\canonical\database-before-reindex`. A reconstrução dos índices foi feita pela tarefa anterior; o registro `database-integrity.json` confirma todas as linhas inalteradas e integrity_check = ok. Após a validação final da interface, uma nova consulta somente leitura confirmou integrity_check = ok e foreign_key_check sem ocorrências. O banco preserva um tema, cinco referências e cinco vínculos de QA.

## Limitações

Não foi determinada a causa original da corrupção dos índices; não foi atribuída à mudança de ordenação. A integridade foi recuperada e verificada, com backup preservado. Não há pendência funcional observada na ordenação. A validação visual publicada foi executada em ACF/NVI; todas as sete versões foram auditadas estruturalmente. O teste visual de 23 páginas pertence à execução anterior, e as duas páginas publicadas foram inspecionadas nesta conclusão.
