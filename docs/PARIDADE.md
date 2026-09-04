# Matriz de paridade — BíbliaTema MAUI → Web

| Recurso | Origem no modelo | Dependência de plataforma | Implementação Web | Estado inicial | Aceite |
|---|---|---|---|---|---|
| Início e verso diário | `MainPage`, `DailyVerseService`, `ReportService` | Shell | `/`, navegação HTML e relógio abstrato | Template | Implementado |
| Leitor | `BibleReaderPage/ViewModel`, `BibleRepository` | Clipboard/Shell | `/biblia`, seleção de intervalo e Clipboard API | Ausente | Implementado |
| Pesquisa bíblica | `BibleSearchPage/ViewModel` | Nenhuma | `/pesquisar-biblia`, consulta parametrizada e limite 100 | Ausente | Implementado |
| Versões | `BibleVersionsPage`, manifesto e validação | FileSystem/picker | `/versoes`, catálogo, ativação e validação dos pacotes | Ausente | Implementado para pacote inicial |
| Comparação | `BibleComparisonPage/ViewModel` | Shell | `/comparacao`, correlação por referência canônica | Ausente | Implementado |
| Temas | `ThemesPage/ViewModel`, `ThemeService` | Diálogo MAUI | `/temas`, CRUD e diálogo compartilhado | Ausente | Implementado |
| Referências | `SavedReferencesPage/ViewModel` | Seletores MAUI | `/referencias`, CRUD canônico e temas N:N | Ausente | Implementado |
| Mensagens | `MessagesPage/ViewModel` | Shell | `/mensagens`, busca, tipos, duplicação e CRUD | Ausente | Implementado |
| Tópicos | `MessageTopicsPage/ViewModel` | Shell | `/mensagens/{id}/topicos`, CRUD e reordenação | Ausente | Implementado |
| Vínculos | `MessageBibleReferencesPage`, `MessageReferencesViewModel` | Sheets MAUI | `/mensagens/{id}/referencias`, vínculo e reordenação | Ausente | Implementado |
| Pesquisa global | `GlobalSearchPage/ViewModel` | Shell | `/pesquisa-global`, grupos selecionáveis | Ausente | Implementado |
| Cards | `VerseCardStudioPage`, `SkiaVerseCardService` | File saver/Pexels | `/cards`, fundos offline, PNG e download seguro | Ausente | Implementado (offline) |
| Relatórios/PDF | `ReportsPage`, `ReportService`, `PdfService` | File saver/share | `/relatorios`, PDF A4 por mensagem e download HTTP | Ausente | Implementado |
| Configurações | `SettingsPage`, `BackupService` | Picker/share | `/configuracoes`, backup, upload e restauração | Ausente | Implementado |
| Erros/diálogos | `BtConfirmDialog`, `BtAlertDialog` | Popup MAUI | `ConfirmButton`, status 400/403/404/409/500 e correlation id | Ausente | Implementado |
| Inicialização local | `MauiProgram`, `AppInitializationService` | App lifecycle | Kestrel loopback, porta configurável e navegador após bind | Aspire template | Implementado |

## Arquitetura

`Biblia.Web.Core` preserva Domain, Application e Infrastructure; `Biblia.Web.Web` contém adaptadores locais, componentes Blazor e endpoints de download. O banco do usuário fica em `%LOCALAPPDATA%\BibliaTema\bibliatema.db`; Bíblias instaladas, backups e cache ficam em subdiretórios graváveis dessa pasta, nunca na publicação.

## Banco e pacote bíblico

Schema atual 2: `SchemaMigration`, `Theme`, `BibleVersionCatalog`, `SavedReference`, `ReferenceTheme`, `Message`, `MessageTopic`, `MessageReference` e `Setting`. As migrations são transacionais/idempotentes e ativam foreign keys/WAL. O pacote inicial contém ACF, ARA, ARC, AS21, KJF, NBV e NVI; NAA, NTLH e NVT permanecem excluídas conforme auditoria do modelo.
