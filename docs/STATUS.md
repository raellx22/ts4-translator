# Status

Última atualização: 2026-08-26.

## Current phase

Fase 1 (Fundação) quase concluída; Fase 2 (Sims4 Core) com o núcleo pronto; Fase 3 (Translation Core)
com o pipeline inteiro pronto (TokenProtector, TranslationValidator, BatchBuilder,
TranslationOrchestrator); Fase 6 (Providers) com o primeiro provider real pronto
(`OpenAICompatibleProvider`, nunca testado contra um servidor real de verdade — só contra HTTP falso
em teste); Fase 7 (Translation Memory) com exact-match pronto; Project System (seção 33) com o núcleo
pronto (`IProjectService`); **primeira tela de produto real escrita, executada de verdade e com o
fluxo completo confirmado funcionando ponta a ponta** (Home: importar `.package` → contar STBLs →
criar projeto → aparecer na lista de recentes). Ver DEC-010: a shell WinUI, Mica, tema, navegação, e
agora o próprio fluxo de importação, foram confirmados com screenshots reais rodando o app como um
pacote MSIX instalado (não via `dotnet run` — ver `docs/BUILD.md` para o procedimento). Dois bugs reais
foram encontrados e corrigidos nesse processo: uma exceção engolida pelo `AsyncRelayCommand`, e o
próprio bloqueio do `FileOpenPicker` (não era bug de código — era o modo de lançamento via `dotnet run`).
Ver `docs/ROADMAP.md` para o detalhamento completo por fase.

## Completed

- Solution `.sln` (formato clássico — DEC-002) com 7 projetos `src/` + 4 `tests/`, todos compilando
  limpo em x64 com `TreatWarningsAsErrors`.
- Stack fixado e documentado (DEC-001): .NET SDK 10.0.400, Windows App SDK 2.4.0,
  LlamaLogic.Packages 3.8.2, CommunityToolkit.Mvvm 8.4.2 (referenciado, ainda não usado em nenhum
  ViewModel real), Microsoft.Extensions.DependencyInjection 10.0.11, EF Core/Sqlite 10.0.11.
- `TokenProtector` (Translation) + `ITokenProtector` (Core) — 23 testes.
- `TranslationValidator` (Translation) + `ITranslationValidator` (Core) — 12 testes.
- `Sims4PackageService`/`SmartSimStringTableService` (Sims4) + `IPackageService`/`IStringTableService`
  (Core) — 11 testes com packages sintéticos, locale PT-BR/EN/FR confirmado por teste real, não por
  suposição (ver DEC-006 — a doc XML do LlamaLogic estava errada sobre o tipo de exceção lançado).
- `SqliteTranslationMemory` (Persistence) + `ITranslationMemory` (Core), EF Core com migration real
  `InitialCreate` — 5 testes de integração.
- `ITranslationProvider`/`ProviderCapabilities`/`ITranslationOrchestrator` (Core) +
  `BatchBuilder`/`TranslationOrchestrator` (Translation) — pipeline completo protege→provider→
  reconcilia por ID→restaura→valida→memory. 14 testes (6 BatchBuilder + 8 orquestrador), com
  providers/memory falsos.
- `OpenAICompatibleProvider` (Providers) — primeiro `ITranslationProvider` real: um
  `POST /chat/completions` por batch contra qualquer endpoint compatível com OpenAI (LM Studio local,
  potencialmente NVIDIA NIM), com recuperação controlada de JSON truncado/malformado da resposta do
  modelo. 9 testes contra um `HttpMessageHandler` falso — **nunca testado contra um servidor real**.
- `IProjectService`/`EfProjectService` (Persistence) — CRUD de projetos (nome, arquivos `.package`,
  par de locales), EF Core com migration real `AddProject`, cascade delete dos arquivos-fonte.
  7 testes de integração contra SQLite real; um deles pegou um bug real do provider SQLite do EF Core
  (não suporta `ORDER BY` em coluna `DateTimeOffset` — corrigido com ordenação client-side, DEC-008).
- `HomePage`/`HomePageViewModel` (App) — primeira tela de produto: importar `.package` via
  `IPathPickerService`, criar `TranslationProject`, contar STBLs via `IPackageService`, listar
  projetos recentes. Resolvido por service locator estático (`App.Services`), não injeção de
  construtor — `Frame.Navigate(Type)` do WinUI não suporta isso nativamente (DEC-009). **Fluxo
  completo confirmado funcionando de ponta a ponta** rodando o app como MSIX instalado: seletor de
  arquivo abre, arquivo `.package` sintético selecionado, STBL contada corretamente, projeto criado e
  exibido na lista de recentes — com screenshot real (ver DEC-010, `docs/BUILD.md`).
- `AppDataPaths` (Infrastructure) resolvendo `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}`.
- Composition root em `App.xaml.cs`: `ServiceCollection` com `AddSims4Services()` +
  `AddTranslationServices()` + `AddPersistenceServices(...)`, migration aplicada no `OnLaunched`.
- Shell WinUI (NavigationView, Mica, título estendido) a partir do template `winui-navview` — sem
  telas de produto ainda.
- Toda decisão relevante registrada em `docs/DECISIONS.md` (DEC-001 a DEC-010).

## In progress

Nada em edição neste momento — todo trabalho iniciado nesta sessão foi levado a um estado compilável
e testado antes de passar para o próximo item.

## Blocked

Nada bloqueado por motivo interno ao app no momento. O bloqueio de verificação do `FileOpenPicker`
documentado em versões anteriores deste arquivo foi resolvido — não era bug de código, era o modo de
lançamento via `dotnet run` (ver DEC-010, `docs/BUILD.md`). Dois itens têm **incerteza de fato
pendente de verificação externa**:

1. Formato exato do prompt esperado por `nvidia/riva-translate-4b-instruct-v2` — duas fontes
   discordam (playground ao vivo vs. NGC catalog). Precisa de uma API key NVIDIA real para testar.
2. Nenhuma receita testada de empacotar Argos Translate sem exigir Python do usuário — vai exigir
   prototipagem própria (ver Fase 4 no roadmap) antes de comprometer a arquitetura.

## Known issues

- `OpenAICompatibleProvider` has never been exercised against a real server (LM Studio, NIM, or
  otherwise) — only against a fake `HttpMessageHandler` in tests. The request/response shape follows
  the standard OpenAI chat-completions contract, but real-world quirks (server timeouts, chunked
  encoding, non-standard headers) are unverified.

- CA1707 (underscore em identificadores) suprimido em `tests/` (DEC-003) — intencional, não é dívida
  técnica.
- CA1861 suprimido em todo o projeto `TS4Translator.Persistence` (DEC-007) por causa do código gerado
  pelo `dotnet ef migrations add` — intencional.
- Regex de tags de markup do `TokenProtector` (`<...>`) é genérico o suficiente para, em teoria,
  capturar um "menor que" seguido de "maior que" que não seja markup real — nunca testado contra STBL
  real do jogo (só contra exemplos sintéticos da própria suíte de testes). Repriorizar se aparecer em
  mods reais.

## Tests currently failing

Nenhum. 81 testes automatizados passando (`dotnet test TS4Translator.sln --filter "FullyQualifiedName!~TS4Translator.App"`).
`TS4Translator.App` não tem testes automatizados de UI (não há framework de UI test configurado), mas
o fluxo principal da `HomePage` foi verificado manualmente com sucesso, rodando o app de verdade —
ver DEC-010.

## Commands to build

Ver `docs/BUILD.md` — inclui build/test normais, como rodar via `dotnet run` para desenvolvimento do
dia a dia, e o procedimento completo (empacotar MSIX, assinar, instalar) necessário para testar
qualquer fluxo que use `FileOpenPicker` ou outra API mediada por broker do Windows.

## How to run the application

Ver `docs/BUILD.md`. Resumo: `dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64`
para desenvolvimento normal; empacotar como MSIX e instalar para testar o picker de arquivos.

## Next milestones

Ver "Próximas tarefas concretas" no final de `docs/ROADMAP.md`. Em ordem:
1. Drag-and-drop de arquivos na `HomePage` (seção 34).
2. Entidade `StringEntry` (nova migration) — strings extraídas ligadas a um `Project`.
3. Testar `OpenAICompatibleProvider` contra um LM Studio local de verdade.
4. Armazenamento seguro de API key (Windows Credential Manager/DPAPI, seção 19).
