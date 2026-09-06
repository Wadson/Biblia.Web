# Auditoria de banco, vínculos e relatório — 06/09/2026

## Resultado

Erro reproduzido com uma cópia consistente do banco real e com o `Biblia.Web.Core.dll` da instalação em `C:\Program Files\Biblia.Web`. A montagem dos 9 temas e 130 vínculos terminou normalmente; a criação do PDF falhou na leitura das configurações de tipografia. O índice `sqlite_autoindex_Setting_1` estava corrompido.

Foi executado somente `REINDEX sqlite_autoindex_Setting_1`, primeiro em cópia e depois no banco real, com o aplicativo fechado. A operação foi transacional e condicionada à confirmação de integridade, chaves estrangeiras e igualdade de todas as linhas de todas as tabelas antes/depois. Nenhum tema, referência, vínculo, observação ou configuração foi removido ou alterado pelo reparo.

Após reabrir a instalação real pelo Windows Explorer, a tela confirmou: **Relatório gerado com 9 tema(s) e 130 referência(s)**. A operação com o banco real foi validada no navegador, não apenas no projeto ou em dados de teste.

## Causa comprovada e evidências

1. `PRAGMA integrity_check` retornou `wrong # of entries in index sqlite_autoindex_Setting_1`.
2. A leitura completa de `Setting NOT INDEXED` retornou seis configurações válidas. A consulta por chave `PdfReportTypography`, que não existia na tabela, lançou `database disk image is malformed` em vez de retornar ausência.
3. A execução dos componentes instalados confirmou a sequência: `ReportService.BuildThemesAsync` concluiu; `PdfService.CreateThemeVersePdfAsync` → `PdfReportSettings.GetPdfTypographyAsync` → `SettingsService.GetAsync` → `SettingsRepository.GetAsync` lançou SQLite Error 11.
4. Após reconstruir o índice, a ausência da configuração passou a retornar normalmente e o PDF foi gerado usando os valores padrão previstos pelo sistema.

Isso identifica a causa imediata do erro. **A origem histórica da corrupção não foi determinada.** Não há evidência suficiente para atribuí-la a hardware, desligamento ou uma restauração específica.

Logs reproduzíveis: [antes](before.log), [depois](after.log). Auditoria estruturada por vínculo: [audit.json](audit.json). Registro do reparo real: [repair-live.json](repair-live.json).

## Dados conferidos

| Item | Resultado |
|---|---|
| Banco real | `C:\Users\WRSoft\AppData\Local\BibliaTema\bibliatema.db` |
| Temas | 9, preservados |
| Referências guardadas | 132, preservadas |
| Vínculos temáticos | 130, preservados |
| Referências distintas vinculadas | 129 |
| Referências sem tema | 3: IDs 1, 9 e 10; não são vínculos órfãos |
| Chaves estrangeiras | Nenhuma violação |
| Bancos bíblicos | ACF, ARA, ARC, AS21, KJF, NBV e NVI: integridade `ok` |
| Versões usadas pelos vínculos | ACF, KJF, NBV e NVI |
| Validação de todos os 130 vínculos | Versão existente, passagem completa, sem números duplicados e sem texto vazio |
| Integridade após reparo | `ok` |
| Comparação das tabelas antes/depois | Todas as linhas idênticas |

A diferença entre 132 referências e 130 vínculos é explicada pelos três registros sem tema e por uma referência presente em mais de um tema. Não foi encontrada perda de referências.

Backup anterior ao reparo: `C:\Users\WRSoft\AppData\Local\BibliaTema\backups\indice-setting-20260906-202856.db`. A cópia original usada na investigação também permanece em `original.db` nesta pasta. Ambos preservam o estado anterior ao reparo, inclusive o defeito do índice; não são backups saudáveis para restauração operacional.

## Por que a validação anterior não encontrou o problema

Há duas instalações com DLLs diferentes: `C:\Program Files\Biblia.Web` e `D:\Publicacao\Biblia.Web`. Além disso, a execução iniciada diretamente pelo ambiente do Codex acessou uma cópia de dados em:

`C:\Users\WRSoft\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\BibliaTema\bibliatema.db`

Essa cópia contém 1 tema e 7 referências de QA. Sua integridade não representa a base real. Abrir a instalação pelo Windows Explorer permitiu validar o banco real, com os mesmos 9/132/130 registros da imagem enviada.

Recomendação: registrar na inicialização a identidade da instalação e contagens da base; não aceitar uma validação funcional sem conferir que os dados correspondem aos do usuário. O caminho lógico sozinho pode ser insuficiente em execução com redirecionamento de arquivos.

## Achados adicionais no código atual

Estes achados foram identificados por inspeção do código. Não foram apresentados como causas comprovadas da corrupção e não foram modificados nesta auditoria.

### Alta prioridade — restauração troca arquivo e remove WAL sem coordenar todas as conexões

`Biblia.Web.Core/Infrastructure/AppDatabase/AppDatabase.cs:44`, especialmente linhas 55–59 e 67–68: `ReplaceAsync` limpa pools, remove os arquivos auxiliares e substitui o banco. A trava de inicialização não abrange toda a vida das conexões devolvidas aos repositórios e não coordena outras instâncias do aplicativo. Há risco de operações simultâneas durante a troca e de perda do estado necessário ao rollback, pois os auxiliares são removidos antes da substituição.

Recomendação: executar a restauração com acesso exclusivo ao banco e todas as instâncias paradas, ou redesenhar o acesso para coordenar conexões ativas e restauração entre processos. Preservar o estado necessário ao rollback antes de qualquer remoção. Esta auditoria evitou esse mecanismo: o reparo foi feito por transação SQLite, sem substituir o arquivo ativo.

### Média prioridade — exibição dos vínculos usa a versão selecionada, PDF usa a versão gravada

`Biblia.Web.Core/Application/Services/ThemeVerseLinkService.cs:55` consulta o texto usando `versionCode` da tela e devolve esse código para todos os itens; não usa `item.Link.BibleVersionId` para escolher a tradução de cada vínculo. Já `ReportService.cs:45` usa a versão persistida no vínculo.

Consequência: ao selecionar uma tradução diferente na tela, o texto exibido pode diferir do relatório, especialmente em temas com traduções misturadas. Se essa tela for uma prévia de comparação, deve identificar essa condição e mostrar também a versão salva; se for uma lista fiel de vínculos, deve ler a versão salva.

### Média prioridade — vinculação e relatório aplicam validações diferentes

`ThemeVerseLinkService.cs:38` rejeita números ausentes ou ambíguos, mas não rejeita texto vazio. `ReportService.cs:55` rejeita texto vazio, mas usa um conjunto de números e não rejeita duplicidade por si só. Assim, um vínculo pode ser aceito e falhar posteriormente, ou uma alteração no banco bíblico pode produzir conteúdo duplicado no relatório.

Recomendação: compartilhar a validação de passagens completas, não vazias e não ambíguas entre os dois fluxos. Nenhum dos 130 vínculos atuais apresentou esses problemas.

### Média prioridade — validação de arquivo de backup não equivale a integridade SQLite

`Biblia.Web.Core/Infrastructure/Files/BackupService.cs:52`: `ValidateAsync` confere estrutura ZIP, tamanho, manifesto e hash; a integridade SQLite é conferida apenas depois, durante `RestoreAsync`, por `ValidateDatabaseAsync`. `CreateAsync` também pode copiar e embalar um banco já corrompido.

Recomendação: validar a integridade do snapshot na criação e na validação explícita de backup, distinguindo backup íntegro de cópia de segurança forense. Um hash correto apenas confirma os bytes copiados.

## Escopo e limites

Foram auditadas a integridade física/lógica do banco real, as sete bases bíblicas, cada um dos 130 vínculos e a execução do gerador instalado antes/depois. A única alteração operacional foi a reconstrução do índice defeituoso; não houve substituição das DLLs da instalação nem mudanças de regras de negócio. Os achados adicionais precisam de correções e testes próprios; a origem histórica da corrupção continua não comprovada.

Ferramentas reproduzíveis estão em `tools/ReportAudit`: o executor .NET usa uma cópia de banco e os componentes instalados; `audit.py` verifica vínculos e testa o reparo numa cópia; `repair_setting_index.py` aborta se encontrar um diagnóstico diferente do que foi confirmado nesta auditoria.
