# Status

Última atualização: 2026-08-26.

## Current phase

O caminho prático completo já funciona de ponta a ponta, verificado rodando o app instalado de
verdade (não só compilando): **importar um `.package` → extrair e persistir cada string → abrir no
Editor → traduzir as pendentes (via um provider compatível com OpenAI configurado em Configurações) →
exportar um package traduzido com validação de round-trip real**. Ver DEC-011 para o relato completo
da verificação.

O único elo desse caminho ainda não exercitado com dados reais é a chamada HTTP de tradução em si —
foi testada com HTTP falso (91 testes) e o fallback "sem provider configurado" foi confirmado no app
de verdade, mas nenhuma sessão até agora teve acesso a um LM Studio local rodando ou a uma API key da
NVIDIA para testar a tradução acontecendo de fato. Essa é a próxima verificação prioritária.

Fora do caminho principal: Fase 0/1/2 essencialmente concluídas; Fase 3 (Translation Core) com o
pipeline inteiro pronto; Fase 6 com o primeiro provider real (`OpenAICompatibleProvider`); Fase 7
(Translation Memory) com exact-match pronto; Fase 9 (Editor) e Fase 11 (Export) com a primeira versão
pronta e verificada. Ver `docs/ROADMAP.md` para o detalhamento completo por fase.

## Completed

- Solution `.sln` (formato clássico — DEC-002) com 7 projetos `src/` + 5 `tests/`, todos compilando
  limpo em x64 com `TreatWarningsAsErrors`.
- Stack fixado e documentado (DEC-001): .NET SDK 10.0.400, Windows App SDK 2.4.0,
  LlamaLogic.Packages 3.8.2, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection
  10.0.11, EF Core/Sqlite 10.0.11.
- `TokenProtector`/`TranslationValidator`/`BatchBuilder`/`TranslationOrchestrator` (Translation) — o
  pipeline de tradução inteiro: protege tokens → chama o provider → reconcilia por ID → restaura →
  valida → grava em translation memory. 84 testes cobrindo essas quatro peças.
- `Sims4PackageService`/`SmartSimStringTableService` (Sims4) — leitura/escrita real de STBL,
  `WriteStringTablesAsync` escrevendo múltiplas STBLs num único package de saída (necessário para
  exportação real de projetos com mais de uma STBL de origem), locale confirmado por teste real, não
  por suposição (DEC-006). 13 testes com packages sintéticos.
- `SqliteTranslationMemory`, `EfProjectService`, `EfProjectStringService`, `EfProviderConfigurationService`
  (Persistence) — SQLite via EF Core, 4 migrations reais (`InitialCreate`, `AddProject`,
  `AddProjectString`, `AddProviderConfiguration`). 28 testes de integração contra SQLite real; um
  pegou um bug real do provider SQLite do EF Core (`ORDER BY` em `DateTimeOffset`, DEC-008).
- `DpapiSecretStore`/`ISecretStore` (Infrastructure) — API key de provider criptografada via DPAPI do
  Windows, nunca em texto puro nem no SQLite (seção 19). 6 testes com criptografia real, em diretório
  isolado.
- `OpenAICompatibleProvider`/`OpenAICompatibleProviderFactory` (Providers) — provider real
  (LM Studio, NVIDIA NIM, qualquer endpoint compatível com OpenAI), construído a partir da
  configuração salva pelo usuário. 11 testes contra um `HttpMessageHandler` falso — **nunca exercitado
  contra um servidor real**.
- `HomePage`, `EditorPage`, `SettingsPage` (App) — as três telas de produto reais. Fluxo completo
  (importar → extrair → traduzir/exportar) confirmado rodando o app instalado como MSIX, com um
  `.package` sintético: 4 strings extraídas e persistidas, editor mostrando todas com status,
  "Traduzir pendentes" mostrando a mensagem correta sem provider configurado, "Exportar" gerando e
  validando (round-trip real) um package traduzido de verdade. Ver DEC-011.
- Navegação Home → Editor com parâmetro (`Frame.Navigate(typeof(EditorPage), project.Id)`), resolvida
  no `OnNavigatedTo` da página — primeira navegação com parâmetro do app.
- Edição manual persistente no Editor: coluna de tradução editável, salva em `LostFocus`, cada
  sobrescrita grava a versão anterior em `ProjectStringRevisionRecord` (histórico, mais recente
  primeiro). Filtro por status e busca de texto na lista de strings. Ver DEC-012.
- Drag-and-drop de `.package` na `HomePage` (além do file picker), unificado com a lógica de import por
  `HomePageViewModel.RunImportAsync`. Reimportar um mod já processado agora reporta quantas strings são
  novas/alteradas/reaproveitadas/removidas (`ImportDiffResult`), e uma tradução existente cujo texto de
  origem mudou é marcada `NeedsReview` em vez de descartada. Ver DEC-012.
- Glossário (`IGlossaryService`/`EfGlossaryService`) integrado ao pipeline de tradução: termos globais
  ou por projeto, `NeverTranslate`, filtrados por batch (só os relevantes ao texto daquele lote são
  enviados ao provider) e incluídos no prompt do `OpenAICompatibleProvider`. Ver DEC-012.
- `GlossaryPage`/`GlossaryPageViewModel` (App) — tela para adicionar/listar/remover termos globais do
  glossário, nova entrada "Glossário" na navegação. Verificada rodando o app de verdade via UI
  Automation (adicionar → persiste após reload → remover → persiste a remoção após reload); um bug
  real de binding no botão "Remover" foi encontrado e corrigido nesse processo. Termos por projeto
  ainda não têm UI (só a global). Ver DEC-013.
- Preset "NVIDIA NIM" em Configurações — preenche endpoint/model conhecidos com um clique, sem afirmar
  que a tradução com esse modelo já foi validada (ainda não foi, ver Fase 5 do ROADMAP). Verificado
  rodando o app de verdade. Ver DEC-014.
- Translation Profiles (seção 22, escopo: instruções livres por projeto, não biblioteca de presets
  nomeados) — `TranslationProject.CustomInstructions`, editável na Editor, propagado ao
  `TranslationOrchestrator`/provider igual ao glossário. Corrigido no processo um gap real: o botão
  "Traduzir pendentes" nunca buscava os termos do glossário para repassar ao orquestrador, apesar de
  toda a integração de glossário (DEC-012) estar pronta e testada em todas as camadas — agora
  `EditorPageViewModel.TranslateAsync` chama `IGlossaryService.GetTermsAsync` antes de traduzir. Ver
  DEC-015.
- Fuzzy matching em Translation Memory (seção 26/63) — `StringSimilarity` (Levenshtein) +
  `ITranslationMemory.FindSimilarAsync`; o orquestrador reaproveita uma entrada ≥97% similar em vez de
  chamar o provider, sempre marcando o resultado para revisão (`FuzzyMatchReused`). Ver DEC-016.
- `AppDataPaths` (Infrastructure) resolvendo `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}`.
- Composition root em `App.xaml.cs` com todos os serviços registrados (`AddSims4Services`,
  `AddTranslationServices`, `AddPersistenceServices`, `AddInfrastructureServices`,
  `AddProviderServices`), migration aplicada no `OnLaunched`.
- Shell WinUI (NavigationView, Mica, título estendido) a partir do template `winui-navview`.
- Toda decisão relevante registrada em `docs/DECISIONS.md` (DEC-001 a DEC-016).

## In progress

Nada em edição neste momento — todo trabalho desta sessão foi levado a um estado compilável, testado,
e verificado rodando o app de verdade antes de parar.

## Blocked

Nada bloqueado por motivo interno ao app. Três itens têm **incerteza de fato pendente de verificação
externa** (não são bloqueios de código, são coisas que só um teste com infraestrutura real resolve):

1. **A tradução via `OpenAICompatibleProvider` nunca rodou contra um servidor real** — nem LM Studio
   local nem NVIDIA NIM. É a lacuna mais importante a fechar antes de considerar o caminho principal
   totalmente verificado. Ver `docs/HANDOFF.md`.
2. Formato exato do prompt esperado por `nvidia/riva-translate-4b-instruct-v2` — duas fontes
   discordam (playground ao vivo vs. NGC catalog). Precisa de uma API key NVIDIA real para testar.
3. Nenhuma receita testada de empacotar Argos Translate sem exigir Python do usuário — vai exigir
   prototipagem própria (ver Fase 4 no roadmap) antes de comprometer a arquitetura.

## Known issues

- `OpenAICompatibleProvider` nunca foi exercitado contra um servidor real — só contra
  `HttpMessageHandler` falso em teste. O formato de requisição/resposta segue o contrato padrão de
  chat-completions da OpenAI, mas particularidades de um servidor real (timeouts, encoding chunked,
  headers não padrão) não foram verificadas.
- CA1707 (underscore em identificadores) suprimido em `tests/` (DEC-003) — intencional.
- CA1861 suprimido em todo o projeto `TS4Translator.Persistence` (DEC-007) por causa do código gerado
  pelo `dotnet ef migrations add` — intencional.
- Regex de tags de markup do `TokenProtector` (`<...>`) é genérico o suficiente para, em teoria,
  capturar um "menor que" seguido de "maior que" que não seja markup real — nunca testado contra STBL
  real do jogo (só contra exemplos sintéticos da própria suíte de testes e um `.package` sintético de
  verificação manual). Repriorizar se aparecer em mods reais.

## Tests currently failing

Nenhum. 144 testes automatizados passando (`dotnet test TS4Translator.sln --filter "FullyQualifiedName!~TS4Translator.App"`).
`TS4Translator.Core.Tests` finalmente tem testes reais (`StringSimilarityTests`) — estava vazio (só
scaffolding do template) desde o início do projeto.
`TS4Translator.App` não tem testes automatizados de UI (não há framework de UI test configurado), mas
o fluxo completo (importar → extrair → editor → exportar com round-trip) foi verificado manualmente
com sucesso, rodando o app de verdade — ver DEC-011. As features desta rodada (edição manual/histórico,
drag-and-drop, diff de reimportação, glossário — DEC-012) foram cobertas por teste automatizado; a
tela de Glossário (DEC-013) também foi verificada rodando o app de verdade. As demais telas novas
(edição manual no Editor, drag-and-drop na Home) **não** foram re-verificadas rodando o app desta vez,
por instrução explícita do usuário: ele vai avaliar e testar tudo pessoalmente no ambiente real depois
desta conclusão, e qualquer problema encontrado entra numa rodada de correção separada.

## Commands to build

Ver `docs/BUILD.md` — inclui build/test normais, como rodar via `dotnet run` para desenvolvimento do
dia a dia, e o procedimento completo (empacotar MSIX, assinar, instalar) necessário para testar
qualquer fluxo que use `FileOpenPicker`/`FileSavePicker` ou outra API mediada por broker do Windows.

## How to run the application

Ver `docs/BUILD.md`. Resumo: `dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64`
para desenvolvimento normal; empacotar como MSIX e instalar para testar picker de arquivo/salvar.

## Next milestones

Ver "Próximas tarefas concretas" no final de `docs/ROADMAP.md`. Em ordem:
1. Testar a tradução com um provider real (LM Studio ou NVIDIA NIM) — ainda pendente, ver DEC-011.
2. UI de glossário por projeto (hoje só o escopo global tem tela — DEC-013).
3. UI de "sugestões de fuzzy match" navegável no Editor (a API já existe com limiar mais permissivo,
   0.85 — DEC-016 — só falta a tela).
4. `TranslationJob`/progresso para lotes grandes, `SettingsPage` em abas reais (seção 41).
