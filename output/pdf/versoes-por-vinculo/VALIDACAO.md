# Versão bíblica por vínculo — validação final

Concluído em 06/09/2026. Publicação: `D:\Publicacao\Biblia.Web`.

- A tela de relatórios contém somente o filtro Tema. A confirmação e ThemeVerseReportRequest também recebem somente o tema.
- O schema 4 adiciona ReferenceTheme.BibleVersionId com chave estrangeira. A migração copia a preferência existente da referência para cada vínculo. Não há inferência de versão histórica quando a preferência está ausente; nesse caso o relatório apresenta mensagem de regularização.
- Novas vinculações gravam a versão selecionada no vínculo, inclusive ao reutilizar uma referência canônica existente. Repetir o vínculo não duplica a referência nem altera a versão originalmente salva naquele vínculo.
- SetThemesAsync conserva vínculos retidos, preservando versão, observação e datas. A alteração da preferência de uma referência não altera a versão dos vínculos já salvos.
- ReportService resolve catálogo e nomes por versão, carrega o texto de cada vínculo na versão correta e mantém BibleCanonicalOrder. Versões ausentes, desabilitadas ou incompatíveis impedem geração parcial, sem substituição silenciosa.
- PDF: removida a versão global no banner e acrescentada a sigla entre colchetes ao lado da referência. Mesma fonte, cards, observações, sumário e mecanismo de paginação.

## Testes

Restore e build Release da solução: sucesso, zero avisos e erros. Suíte final: 104 aprovados, zero falhas, zero ignorados. TRX: `D:\Projetos\Biblia.Web\tmp\link-version\test-results\versoes-release-final.trx`.

Novos testes em LinkedVersionReportTests: migração de schema 3 com preferência presente/ausente; idempotência; conservação de observações; versões diferentes para referência compartilhada por dois temas; alteração posterior de preferência; não duplicação; filtro de tema; texto real ARA/NVI; sequência canônica e siglas no PDF; versão indisponível e vínculo sem versão. Testes existentes de ordem dos 66 livros, tipografia, conteúdo extenso, sumário, paginação e concorrência passaram.

Foi ajustada uma asserção antiga de PDF que tratava menções no sumário como se fossem cabeçalhos do corpo da página. Uma repetição intermediária da suíte não pôde substituir PDFs existentes; a execução final escreveu as evidências em pasta nova e passou integralmente.

## Publicação e banco

As duas instâncias antigas foram encerradas antes da migração. Backup consistente: `D:\Projetos\Biblia.Web\tmp\link-version\bibliatema-before-schema4.db`.

A comparação antes/depois confirmou preservação dos dados anteriores de Theme, SavedReference e ReferenceTheme, incluindo observações e datas. Schema final 4; integrity_check = ok e foreign_key_check sem ocorrências. Evidência: `D:\Projetos\Biblia.Web\tmp\link-version\migration-verified.json`.

Publish Release realizado em `D:\Publicacao\Biblia.Web`. Hashes das DLLs Core e Web publicados iguais aos binários Release testados. A cópia em `C:\Program Files\Biblia.Web` não foi atualizada; o destino solicitado e executado é a publicação em D:.

## QA na interface publicada

A tela foi conferida sem seletor global de versão. No tema de QA existente foram incluídos Hebreus 12:14 em NVI e Hebreus 10:5 em ARA, pela interface real. O banco confirmou as duas versões gravadas individualmente. São sete referências/vínculos no tema de QA após essas duas inclusões.

O PDF publicado tem duas páginas, ambas renderizadas e inspecionadas. Texto extraído confirmou: Gênesis, Salmos, Mateus, João, Hebreus 10:5 [ARA], Hebreus 12:14 [NVI], Apocalipse; a observação anterior de João permaneceu no card correto. Não há metadado global de versão.

PDF publicado: `D:\Projetos\Biblia.Web\output\pdf\versoes-por-vinculo\qa-publicado-versoes-mistas.pdf`.

QA automatizado isolado com ARA/NVI: `D:\Projetos\Biblia.Web\output\pdf\versoes-por-vinculo\validado\qa-versoes-por-vinculo.pdf`.
