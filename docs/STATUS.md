# Status

Última atualização: 2026-08-26.

## Current phase

Fase 1 (Fundação) quase concluída; Fase 2 (Sims4 Core) com o núcleo pronto; Fase 3 (Translation Core)
com as duas peças mais críticas (TokenProtector, TranslationValidator) prontas; Fase 7 (Translation
Memory) com exact-match pronto. Ver `docs/ROADMAP.md` para o detalhamento completo por fase.

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
- `AppDataPaths` (Infrastructure) resolvendo `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}`.
- Composition root em `App.xaml.cs`: `ServiceCollection` com `AddSims4Services()` +
  `AddTranslationServices()` + `AddPersistenceServices(...)`, migration aplicada no `OnLaunched`.
- Shell WinUI (NavigationView, Mica, título estendido) a partir do template `winui-navview` — sem
  telas de produto ainda.
- Toda decisão relevante registrada em `docs/DECISIONS.md` (DEC-001 a DEC-007).

## In progress

Nada em edição neste momento — todo trabalho iniciado nesta sessão foi levado a um estado compilável
e testado antes de passar para o próximo item.

## Blocked

Nada bloqueado por motivo externo real ainda. Dois itens têm **incerteza de fato pendente de
verificação empírica** antes de implementar (não são bloqueios, são riscos documentados):

1. Formato exato do prompt esperado por `nvidia/riva-translate-4b-instruct-v2` — duas fontes
   discordam (playground ao vivo vs. NGC catalog). Precisa de uma API key NVIDIA real para testar.
2. Nenhuma receita testada de empacotar Argos Translate sem exigir Python do usuário — vai exigir
   prototipagem própria (ver Fase 4 no roadmap) antes de comprometer a arquitetura.

## Known issues

- CA1707 (underscore em identificadores) suprimido em `tests/` (DEC-003) — intencional, não é dívida
  técnica.
- CA1861 suprimido em todo o projeto `TS4Translator.Persistence` (DEC-007) por causa do código gerado
  pelo `dotnet ef migrations add` — intencional.
- Regex de tags de markup do `TokenProtector` (`<...>`) é genérico o suficiente para, em teoria,
  capturar um "menor que" seguido de "maior que" que não seja markup real — nunca testado contra STBL
  real do jogo (só contra exemplos sintéticos da própria suíte de testes). Repriorizar se aparecer em
  mods reais.

## Tests currently failing

Nenhum. 51 testes passando (`dotnet test TS4Translator.sln --filter "FullyQualifiedName!~TS4Translator.App"`).
O projeto `TS4Translator.App` não tem testes automatizados de UI ainda (não há framework de UI test
configurado) e não foi executado visualmente nesta sessão — sem acesso a um desktop Windows interativo
neste ambiente de execução, o build foi validado apenas por `dotnet build`, não por rodar a janela.

## Commands to build

```powershell
dotnet restore TS4Translator.sln
dotnet build TS4Translator.sln --configuration Release -p:Platform=x64
```

## Commands to test

```powershell
dotnet test TS4Translator.sln --configuration Release --no-build -p:Platform=x64
```

(Em Debug, sem `--no-build`, funciona igual — `-p:Platform=x64` é necessário por causa do projeto
WinUI, que não é `AnyCPU`.)

## How to run the application

```powershell
dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64
```

Nunca executado interativamente nesta sessão (sem acesso a um desktop Windows no ambiente de execução
usado para desenvolver). **A próxima sessão com acesso a um desktop Windows real deveria rodar isso
primeiro**, antes de qualquer outra coisa, para confirmar que a shell WinUI realmente abre.

## Next milestones

Ver "Próximas tarefas concretas" no final de `docs/ROADMAP.md`. Em ordem:
1. `ITranslationProvider` + provider mock.
2. `BatchBuilder` + orquestrador Protect→Provider→Restore→Validate→TranslationMemory.
3. Entidade `Project` (nova migration) + `IProjectService`.
4. Primeira tela de produto real (importar `.package`, mostrar contagem de strings).
