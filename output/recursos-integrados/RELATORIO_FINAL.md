# Conclusão e validação — 07/09/2026

Publicação concluída em `D:\Publicacao\Biblia.Web`, projeto `Biblia.Web.Web`, Release. O AppHost Aspire foi compilado como parte da solução, mas não publicado. Os testes usaram bases isoladas; a base pessoal não foi usada para importação, restauração ou criação de temas de QA nesta conclusão.

## Arquitetura e comportamento

Os quatro recursos estão integrados por uma sequência persistente de conteúdo. Schema 5, migração incremental com `Theme.OrderingMode` e `ThemeContent` (tema, posição, referência ou bloco JSON, datas). A migração preserva vínculos e o modo canônico como padrão. Há integração com inclusão e exclusão de vínculos e com a geração do relatório.

`ThemeContentService` centraliza leitura, edição, movimentos e normalização transacional das posições. O modo manual mantém a sequência integral. Ao voltar ao canônico, mediante confirmação, os blocos mantêm os espaços e apenas os versículos são ordenados por `BibleCanonicalOrder`. Observações dos vínculos continuam separadas dos blocos.

Importação: seleção de `.sqlite`/`.db`, código/nome controlados, confirmação, estado de processamento, cancelamento, validação estrutural/canônica, limite de 256 MB e SHA-256. Não sobrescreve código existente. A interface permite ativar, habilitar/desabilitar, validar e remover importadas; dependências de vínculos são protegidas. Bancos importados são usados em leitura.

Backup formato 2: `manifest.json`, `bibliatema.db` e `bibles/{CODE}.sqlite`. Por versão: código, nome, entrada, tamanho, SHA-256, schema, idioma, habilitada, ativa, empacotada e status de validação. Limites: 128 versões, 256 MB por Bíblia, 8 GB totais; banco principal até 250.000.000 bytes; manifesto até 1.000.000 bytes; razão de compressão até 500. Valida nomes, duplicatas, declaração no manifesto, hashes, tamanhos, integridade SQL, chaves estrangeiras, catálogo e estado ativo.

Restauração: validação em diretório temporário, backup de segurança completo, nova geração imutável de arquivos bíblicos, reconstrução dos caminhos e substituição transacional SQLite do banco. Consultas reais verificam as versões após a troca. Em falha, recupera o snapshot anterior e remove a geração incompleta. Arquivos anteriores e extras são preservados para recuperação; ficam fora do catálogo restaurado. Essa política conserva arquivos em disco, sem limpeza automática definitiva.

Backups antigos continuam aceitos e preservam/reconciliam arquivos locais por código, com aviso de que não continham Bíblias. Operações de arquivo usam coordenação por diretório de dados.

## Verificações executadas

| Verificação | Resultado |
|---|---|
| Restore da solução | Sucesso, dependências atualizadas |
| Build Release da solução, incluindo AppHost | Sucesso, zero avisos e zero erros |
| Testes Debug | 144 aprovados, 0 falhas, 0 ignorados |
| Testes Release | 144 aprovados, 0 falhas, 0 ignorados |
| Publish Release Web | Sucesso no destino final |
| Execução publicada | Inicialização schema 5 e HTTP local na porta 52611 |
| Importação pela interface publicada | QAFINAL23 instalada/habilitada imediatamente |
| Backup pela interface publicada | Formato 2, oito versões, dez entradas, ACF ativa |
| Restauração pela interface publicada | Sucesso com backup de segurança e consultas validadas |
| Sequência recuperada | Dois versículos, dois blocos, notas, versão QA e ordem manual preservados |
| Setas e edição publicada | Bloco movido, limite superior desabilitado, bloco multilinha criado |
| Validação de texto | Bloco vazio rejeitado; marcador e negrito preservados |
| HTML literal | Texto `<script>` exibido como texto; nenhum elemento script criado por ele |
| Retorno ao canônico | Confirmação acionada pelo teclado; modo salvo; Gênesis antes de Apocalipse |
| Responsividade | Viewport 390×844, sem transbordamento horizontal da página de vinculação |
| PDF pela interface publicada | Gerado após restauração/edição/retorno canônico; conferido visualmente |
| Logs da instância publicada | Sem saída de erro |

A suíte cobre regressão canônica, versões por vínculo, importação válida e inválida, duplicatas, cancelamento, estilos/marcadores, concorrência em movimentos, limites, cascata, PDF longo, backup com mais de vinte entradas, backup antigo e rollback após falha simulada. Foram adicionados dez casos nesta conclusão: sete adulterações de manifesto, cancelamento de backup/validação/restauração, isolamento entre temas e preservação física de versão extra após restauração completa. A primeira execução detectou um nome de tema duplicado na preparação do novo teste; o teste foi corrigido e ambas as suítes passaram.

O runner `tools/IntegratedQa` foi ampliado para comparar a sequência integral antes/depois da restauração e gerar PDF novamente. Ele executou com uma cópia da DLL efetivamente publicada, confirmada por SHA-256 igual: `E1157CDA1802123C294668C5796A9398E762AC4E5955637E82DD9BB1E5DA2110`.

Uma tentativa inicial de teste da solução com paralelismo padrão não progrediu no ambiente. A execução final usou o projeto que contém toda a suíte, com MSBuild limitado a um processo. O build completo da solução também foi validado dessa forma.

## Evidências

Todos os caminhos abaixo são relativos a `D:\Projetos\Biblia.Web\output\recursos-integrados`:

- `ARQUIVOS_ALTERADOS.txt`: inventário de fontes alteradas/criadas.
- `INVENTARIO.md`: caracterização dos recursos anteriores.
- `tests/final-debug.trx` e `tests/final-release.trx`: resultados completos.
- `qa-publicado/backup-interface-8-versoes.zip`: backup criado pela tela publicada.
- `qa-publicado/backup-completo.zip`: backup com sequência mista do runner.
- `qa-publicado/canonico.pdf`, `manual.pdf`, `restaurado.pdf`: PDFs gerados com a DLL publicada.
- `qa-publicado/interface-canonico.pdf`: PDF gerado pela própria interface publicada, com cinco itens e dois versículos.
- `qa-publicado/restaurado.png` e `interface-canonico.png`: renderizações conferidas visualmente.
- `qa-publicado/hashes-core.txt`: identidade da DLL testada/publicada.
- `published-ui.log` e `published-ui-error.log`: execução publicada e operações reais de QA.
- `qa/`: primeiros PDFs e ZIP da etapa anterior, preservados.

Bases isoladas: `tmp/integrated-final-services` (runner) e `tmp/integrated-published-final` (executável publicado), dentro do projeto. O diretório de publicação contém somente as sete Bíblias empacotadas no catálogo original; as cópias QA não foram incorporadas ao conteúdo publicado.

## Limites da validação

Não existe uma quantidade finita de testes que cubra todas as combinações possíveis. Foram executados todos os testes automatizados disponíveis e os fluxos principais publicados, com casos negativos adicionais. Não foram simulados corte físico de energia, disco cheio, ZIP real de 8 GB, todas as combinações de sistemas/navegadores ou carga prolongada. A conferência de acessibilidade foi funcional por teclado e controles; não é uma certificação completa. O aplicativo atual informa tema claro único: não há alternância escura para testar. O teste de layout em celular cobriu a vinculação, não todas as telas em todos os tamanhos.

Não restaram falhas nos testes executados. A aplicação continua dependente do ASP.NET Core Runtime 10. A preservação das gerações anteriores de Bíblias pode aumentar o consumo de disco após sucessivas restaurações.
