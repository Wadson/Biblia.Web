# Sumário automático do relatório temático - entrega

Projeto: D:\Projetos\Biblia.Web
Publicação: D:\Publicacao\Biblia.Web

## Implementação retomada e validada

O código existente da modernização foi preservado. IPdfService, ThemeVerseReport, ThemeVerseReportSection, ThemeVerseReportReference e fontes Open Sans incorporadas foram confirmados. A versão modernizada usa a composição vetorial do PDFsharp, do pacote PDFsharp-MigraDoc 6.2.4.

PdfService.cs inclui AddTableOfContents, AddThemeTableOfContentsEntry, CreateThemeBookmarkName, RegisterThemeDestination e CompleteTableOfContents. O sumário aparece após o painel inicial e antes dos temas, mantém a mesma ordem de Sections e inclui temas sem referências. Nomes longos quebram em linhas, com contagem de referência/referências, guia pontilhada e coluna numérica fixa à direita. Os textos continuam selecionáveis.

Cada cabeçalho inicial registra um destino PDF nativo theme-{ID}-section-{posição}, combinando ID e índice para evitar colisões. Registra-se a página física já criada e a coordenada do cabeçalho. Após compor todo o corpo, uma segunda passagem escreve os números reais na coluna reservada. Não há estimativas por versículos, números fixos, reinício de numeração nem repaginação causada pelos números. Todas as páginas do sumário já estão no documento. Links internos e favoritos apontam ao mesmo cabeçalho; continuações não registram outra entrada.

A4 retrato, margens espelhadas de 58 pontos internos (20,46 mm) e 40 externos (14,11 mm), rodapé Página X de Y em todas as páginas. Layout das telas e cards preservado.

## Arquivos envolvidos

- Biblia.Web.Core/Infrastructure/Files/PdfService.cs: sumário, destinos, links, favoritos, margens e numeração final.
- Biblia.Web.Core/Application/Services/ReportService.cs: alterações de integridade já existentes da modernização, preservadas.
- Biblia.Web.Tests/Infrastructure/ThemeTableOfContentsTests.cs: sete casos executados, incluindo teoria com 1, 5 e 50 temas; paginação, nomes e IDs duplicados, crescimento do conteúdo, singular e relatório vazio.
- Biblia.Web.Tests/Application/ThemeReportServiceTests.cs: testes existentes de integridade e filtros preservados.
- Biblia.Web.Tests/Biblia.Web.Tests.csproj: fixture compartilhada e PdfPig para validação independente.
- tools/ThemeReportQa/ThemeReportQaData.cs: 50 temas, 213 referências sintéticas, tema vazio, nomes extensos e observações longas.
- tools/ThemeReportQa/Program.cs e ThemeReportQa.csproj: geração contra a DLL publicada.
- tools/ThemeReportQa/verify_toc.py: auditoria independente de todos os destinos, números, ordem, fontes e referências.
- docs/RELATORIO_SUMARIO_AUTOMATICO.md: este registro.

## Validação desta retomada

- dotnet restore Biblia.Web.slnx: sucesso.
- dotnet build Biblia.Web.slnx --no-restore: zero avisos e erros.
- dotnet test Biblia.Web.slnx --no-restore: 64 aprovados, zero falhas e ignorados.
- dotnet build Biblia.Web.slnx -c Release --no-restore: zero avisos e erros.
- dotnet test Biblia.Web.slnx -c Release --no-build --no-restore: 64 aprovados, zero falhas e ignorados.
- dotnet publish Biblia.Web.Web/Biblia.Web.Web.csproj -c Release --no-restore -o D:\Publicacao\Biblia.Web: sucesso.

QA final: output/pdf/sumario/relatorio-sumario-qa.pdf, 50 temas, 213 referências sintéticas representando versículos individuais, 61 páginas. Sumário nas páginas 1, 2, 3 e 4; primeiro tema começa na página 4 imediatamente após o sumário. Todos os 50 números impressos foram comparados com a página e as letras do cabeçalho de destino. Todos os 50 links e favoritos foram conferidos estruturalmente. A ordem e a unicidade foram verificadas, assim como as 213 referências e os rodapés de todas as páginas. Evidência por entrada: output/pdf/sumario/conferencia-paginas.csv.

As 61 páginas foram renderizadas por Poppler. Revisão visual completa nos painéis tmp/pdfs/sumario/inspecao-01.png a inspecao-16.png: alinhamento, pontilhados, nomes longos, continuidade, margens, rodapés, ausência de sobreposição e páginas vazias injustificadas. As 61 sequências de desenho do PDF recém-gerado pela publicação foram comparadas e são idênticas às do PDF desses painéis. Nova renderização também concluída na pasta de visualizações da tarefa.

## Publicação e execução

O projeto executável correto é Biblia.Web.Web. O Blazor registra os serviços e gera o PDF localmente. O AppHost é de orquestração de desenvolvimento e a API de exemplo não participa do relatório; não são necessários artefatos separados neste fluxo.

O gerador de QA carregou explicitamente D:\Publicacao\Biblia.Web\Biblia.Web.Core.dll e gerou o relatório fora de bin/obj. A auditoria confirmou Open Sans TrueType incorporada em todas as páginas. O executável publicado foi iniciado temporariamente na porta 5198 em ambiente Testing, com navegador automático desativado. GET /relatorios respondeu HTTP 200; o processo de teste foi encerrado. A DLL auxiliar de QA foi removida da publicação após a validação.

## Limites da validação

A geração foi exercitada diretamente pelo serviço publicado; a tela foi verificada por HTTP. Os links foram validados pelos destinos PDF, sem clique manual em um leitor. A amostra usa texto sintético, não transcrição bíblica. Nomes patológicos que excedam uma página inteira de sumário são rejeitados com mensagem explícita. A publicação permanece dependente do ASP.NET Core Runtime 10 e foi feita sobre a pasta existente, sem limpeza de arquivos antigos. Não houve necessidade de alterar novamente o código funcional nesta retomada: o trabalho pendente era validar e publicar a implementação encontrada.
