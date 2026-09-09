# Personalização das fontes do relatório PDF

Implementação e validação em 06/09/2026, no projeto D:\Projetos\Biblia.Web.
Publicação final: D:\Publicacao\Biblia.Web (Biblia.Web.Web.exe).

## Alterações

- Biblia.Web.Core/Application/Services/PdfReportTypographyOptions.cs: opções imutáveis, padrões, limites, incrementos de 0,5 pt, validação, normalização e extensões tipadas de ISettingsService.
- Biblia.Web.Core/Infrastructure/Files/PdfService.cs: consulta das configurações antes de cada geração, fontes separadas, entrelinhas medidas, reserva inicial do card e título OBSERVAÇÃO: em todos os fragmentos.
- Biblia.Web.Web/Components/Pages/Settings.razor: inclusão da seção na tela existente.
- Biblia.Web.Web/Components/Shared/PdfTypographySettings.razor: três controles numéricos, prévia reativa, salvamento, mensagens em português e restauração com ConfirmButton.
- Biblia.Web.Web/Components/Shared/PdfTypographySettings.razor.css: apresentação da prévia, foco de teclado e suporte à preferência de cor escura na nova seção; a prévia mantém as cores do papel do PDF.
- Biblia.Web.Tests/Infrastructure/PdfTypographyTests.cs: 27 casos novos de validação, persistência, geração, independência e concorrência.
- Biblia.Web.Tests/Infrastructure/ThemeTableOfContentsTests.cs: reutilização do verificador de sumário e tolerância de 0,01 pt para a diferença de serialização entre coordenadas de links e marcadores.
- tools/ThemeReportQa/Program.cs e ThemeReportQa.csproj: três cenários contra a DLL publicada, banco de QA isolado e reabertura em outro processo.
- docs/RELATORIO_PERSONALIZACAO_FONTES_PDF.md: este registro.

Alterações preexistentes no relatório moderno, sumário e demais arquivos foram preservadas. Artefatos de build já presentes em bin/obj não foram limpos nem revertidos.

## Configurações e persistência

| Propriedade | Padrão | Mínimo | Máximo |
|---|---:|---:|---:|
| PdfReferenceFontSize | 10 pt | 8 pt | 24 pt |
| PdfVerseTextFontSize | 9,5 pt | 8 pt | 22 pt |
| PdfObservationFontSize | 9,5 pt | 7 pt | 20 pt |

Padrões extraídos do código existente. Incrementos: 0,5 pt. O título da observação mantém a proporção anterior (7,5 pt no padrão), com piso de 7 pt.

Persistência pelo ISettingsService/SettingsRepository já existentes, na tabela Setting do SQLite. Os três campos formam um único JSON na chave PdfReportTypography. Uma única operação UPSERT torna o salvamento e a leitura do conjunto atômicos. Não há nova tabela nem migração de esquema. Os dados da aplicação continuam em %LOCALAPPDATA%\BibliaTema\bibliatema.db, fora de bin, obj e da publicação.

Ausência da chave usa os padrões sem gravar ou modificar configurações existentes. Gravações tipadas rejeitam valores inválidos; campos armazenados fora da faixa recebem fallback independente na leitura, e JSON malformado recebe os padrões. Restaurar grava somente esta chave. O teste preserva outra chave com valor dark.

## PDF e paginação

O motor existente é PDFsharp, não o compositor MigraDoc. Fontes e coordenadas já usam pontos, sem conversão adicional. O serviço recebe ISettingsService por DI e captura um conjunto imutável por relatório. As fontes do sumário, temas, título, metadados, contagens e rodapés continuam independentes das opções.

Medição, quebra de linhas, altura dos cards e fragmentação usam as fontes e entrelinhas efetivas. Os números dos versículos usam semibold no mesmo tamanho do texto. Os números do sumário continuam sendo preenchidos depois da renderização completa. Conteúdo maior que uma página continua em cards identificados, preservando o texto integral e repetindo a referência como contexto.

O título visual antigo OBSERVAÇÃO DO VÍNCULO não está mais na composição. Todos os blocos usam OBSERVAÇÃO:. O conteúdo cadastrado não é modificado, mesmo quando contém a expressão do vínculo.

## Compilação, testes e publicação

- dotnet restore Biblia.Web.slnx: sucesso.
- dotnet build Biblia.Web.slnx --no-restore: sucesso, zero avisos e erros.
- dotnet test Biblia.Web.slnx --no-restore: 91 aprovados, zero falhas e ignorados.
- dotnet build Biblia.Web.slnx -c Release --no-restore -m:1: sucesso, zero avisos e erros.
- dotnet test Biblia.Web.slnx -c Release --no-build --no-restore: 91 aprovados, zero falhas e ignorados na rodada final.
- dotnet publish Biblia.Web.Web/Biblia.Web.Web.csproj -c Release --no-restore -o D:/Publicacao/Biblia.Web: sucesso.
- git diff --check nos arquivos de código: sem erros de whitespace.

Uma tentativa de Release encontrou um bloqueio transitório do arquivo rjsmrazor.dswa.cache.json; a repetição serial concluiu normalmente. As primeiras rodadas também permitiram corrigir referências de nomes nos testes/runner e a comparação exata de coordenadas decimais. Resultados acima são os finais.

Casos cobertos: padrões, três campos salvos, recriação de serviços sobre banco existente, restauração, limites individuais inferiores e superiores, zero, negativos, NaN, infinitos, passo inválido, texto/JSON inválido, mínimos, máximos, decimais, alteração independente, números em negrito, texto longo integral, título visual, limites geométricos, sumário com links e marcadores, recálculo em novas gerações, quatro gerações simultâneas com configurações e teste existente com oito gerações/fontes incorporadas.

## QA da publicação

O executável de QA foi compilado referenciando D:\Publicacao\Biblia.Web\Biblia.Web.Core.dll. A execução informou esse caminho de assembly, gerou os três PDFs com as configurações persistidas em output/pdf/fontes/qa-settings.db e um segundo processo confirmou o valor máximo salvo, gerando novamente. Não foram gravadas configurações de QA no banco do usuário.

| PDF | Páginas | Temas | Referências | Páginas de sumário |
|---|---:|---:|---:|---:|
| D:\Projetos\Biblia.Web\output\pdf\fontes\fontes-padrao.pdf | 27 | 26 | 96 | 2 |
| D:\Projetos\Biblia.Web\output\pdf\fontes\fontes-minimo.pdf | 26 | 26 | 96 | 2 |
| D:\Projetos\Biblia.Web\output\pdf\fontes\fontes-maximo.pdf | 89 | 26 | 96 | 2 |

Dados sintéticos identificados como QA: referência longa, temas longos, textos extensos, observações curtas e observações que atravessam várias páginas. Não são transcrições bíblicas correspondentes às referências sintéticas.

Todas as 142 páginas foram renderizadas com Poppler em PNG a 1100 pixels e inspecionadas visualmente em folhas de contato. Conferidos cards, quebras, observações, títulos, margens e rodapés; sem cortes, sobreposições ou páginas vazias. Páginas parcialmente preenchidas resultam da preservação dos cards que cabem inteiros na próxima página.

Auditoria independente com pypdf: 96 marcadores de referência em cada PDF, 26 marcadores de tema, fontes TrueType incorporadas em todas as páginas, ausência do título antigo e presença de OBSERVAÇÃO:. Auditoria com pdfplumber: todos os 26 números e links de cada sumário apontam para cabeçalhos reais de tema; todos os caracteres permanecem dentro das margens e limites verticais.

A aplicação publicada foi iniciada temporariamente em http://127.0.0.1:5198. No navegador foram conferidos carregamento dos padrões, prévia reagindo a 24/22/20, bloqueio do valor 25 com mensagem em português, confirmação de restauração e cancelamento. Reabrir a página recuperou 10/9,5/9,5, sem salvar as alterações de prévia. O banco real permanece preservado. A persistência após reboot é sustentada pelo arquivo SQLite permanente; não foi necessário reiniciar fisicamente o computador.

O projeto executável publicado é Biblia.Web.Web.csproj. O frontend registra e executa os serviços localmente. A API de exemplo e o AppHost Aspire não participam deste fluxo e não foram publicados. Mantida a distribuição existente dependente do runtime .NET 10.

O fluxo real da tela Relatórios também gerou um PDF com o banco atual vazio (0 temas/0 referências). O link de download respondeu HTTP 200 e application/pdf. Os cenários extensos com os três tamanhos foram exercitados pelo runner contra a mesma DLL publicada. Os hashes SHA-256 de Core e Web publicados correspondem aos binários Release validados.
