# Architecture

Uma nova sessão (humana ou IA) deve conseguir entender o sistema lendo só este arquivo, mais
[`DECISIONS.md`](DECISIONS.md) para o porquê de cada escolha relevante.

## Princípio central

A UI (`TS4Translator.App`) nunca conhece detalhes de LlamaLogic.Packages, DBPF, STBL, ou de qual
provider de tradução está em uso. Ela depende só de interfaces definidas em `TS4Translator.Core`.
Isso é reforçado fisicamente: `Core` não tem `PackageReference` para nenhuma biblioteca de terceiros
além do BCL, e nenhum outro projeto pode remover essa restrição sem editar `Core.csproj`.

```text
TS4Translator.App (WinUI 3, ViewModels, DI)
        │  depende de
        ▼
TS4Translator.Core (interfaces + modelos de domínio, zero dependências externas)
        ▲                    ▲                    ▲
        │ implementa         │ implementa         │ implementa
TS4Translator.Sims4   TS4Translator.Translation   TS4Translator.Providers
   (LlamaLogic.Packages)   (TokenProtector,          (Argos, NVIDIA NIM,
                             Validator, Batching)      OpenAI-compatible, ...)
        │
        ▼
TS4Translator.Persistence (SQLite)     TS4Translator.Infrastructure (logging, credenciais, HTTP)
```

`Providers` referencia só `Core`, não `Translation` — um provider concreto nunca vê texto com tokens
intactos nem é responsável por protegê-los. Ele recebe `TranslationRequestItem.ProtectedText` (já
protegido) e devolve `TranslationResultItem.TranslatedProtectedText` (ainda protegido); todo o
proteger→chamar→restaurar→validar fica em `TranslationOrchestrator` (`Translation`), que é quem
efetivamente usa um `ITranslationProvider`, não o contrário. Isso mantém cada provider o mais burro
possível, por design — nenhum provider é confiável para preservar tokens corretamente (seção 9).

## Projetos

### TS4Translator.Core

Interfaces e DTOs puros. Nenhuma implementação, nenhuma dependência de terceiros. Organizado por
área funcional em subpastas:

- `Tokens/` — `ITokenProtector`, `ProtectedString`, `ProtectedToken`, `TokenRestoreResult`,
  `TokenValidationIssue(Kind)`. Ver DEC-004 para o design de proteção/restauração.
- `Validation/` — `ITranslationValidator`, `ValidationIssue`, `ValidationSeverity` (Info/Warning/Error/Critical),
  `ValidationIssueCode`.
- (Planejado, ainda não criado) `Packages/`, `Translation/` (provider/job contracts), `Memory/`,
  `Glossary/`, `Projects/` — serão adicionados quando as fases correspondentes do roadmap começarem,
  não antecipadamente (ver seção 60 do brief original: evitar abstração antes de haver uma implementação
  real para validá-la).

### TS4Translator.Translation

Implementações que não dependem de nenhum SDK de terceiros nem de I/O:

- `TokenProtector : ITokenProtector` — extração por regex de tokens `{...}`, tags `<...>` e quebras
  de linha; reconciliação por identidade de placeholder. Ver DEC-004.
- `TranslationValidator : ITranslationValidator` — funde os problemas do `TokenRestoreResult` com
  checagens próprias (vazio, idêntico ao original, discrepância de tamanho, markup desbalanceado).
- `BatchBuilder : IBatchBuilder` — agrupa itens por contagem e por total de caracteres; um item maior
  que o limite sozinho vira seu próprio batch em vez de ser cortado.
- `TranslationOrchestrator : ITranslationOrchestrator` — o pipeline completo da seção 10/11: consulta
  `ITranslationMemory` primeiro (pula o provider quando já existe tradução exata); para o restante,
  agrupa em batches, protege tokens, chama o provider, **reconcilia cada resultado pelo `Id` do item**
  (nunca por posição/contagem — se o provider omitir ou duplicar um `Id`, o item vira uma falha
  explícita com `ValidationIssueCode.ProviderResultMissing`/`ProviderResultDuplicated`, nunca um
  deslocamento silencioso de traduções), restaura tokens, valida, e só grava na translation memory os
  itens sem problema crítico.

Perfis de tradução (seção 22) ainda não existem — `TranslationOrchestrator` não aceita nenhum
prompt/estilo customizado hoje, e todo `TranslationMemoryEntry` novo grava `ProfileName: null`.

### TS4Translator.Sims4

Encapsula `LlamaLogic.Packages` (referenciado apenas aqui) atrás de `IPackageService`/`IStringTableService`
definidos em `Core`. Nenhum tipo de LlamaLogic cruza a fronteira deste projeto — ver o princípio
central acima e a seção 6 do brief.

- `Sims4PackageService : IPackageService` — abre/lê/escreve STBL via `DataBasePackedFile`, sempre
  para um caminho de saída distinto do original (nunca sobrescreve o mod).
- `SmartSimStringTableService : IStringTableService` — locale de STBL via
  `LlamaLogic.Packages.SmartSimUtilities` (não reimplementado à mão — ver DEC-006).
- `ServiceCollectionExtensions.AddSims4Services()` registra ambos como singletons.

11 testes de integração com packages sintéticos (round-trip escrita→leitura, locale PT-BR/EN/FR,
saída já existente não é sobrescrita).

### TS4Translator.Providers

- `OpenAICompatible.OpenAICompatibleProvider : ITranslationProvider` — um `POST /chat/completions`
  por batch, contra qualquer endpoint compatível com OpenAI (LM Studio local, NVIDIA NIM, e outros —
  seção 17). O batch inteiro vai como um array JSON `[{id, text}]` na mensagem do usuário; o system
  prompt instrui o modelo a responder só com `[{id, translation}]`, preservando qualquer
  `__TS4_TOKEN_NNNN__` literalmente. **Nunca confia que a resposta é JSON válido ou completo**
  (seção 46): se o array vier truncado ou malformado, cada objeto `{...}` completo é recuperado
  individualmente via um scanner de profundidade de chaves — objetos incompletos são descartados, não
  travam o batch inteiro. 9 testes com um `HttpMessageHandler` falso (nenhum servidor real é
  necessário nem contatado — seção 44).
- Ainda faltam: Argos Translate (Fase 4, bloqueado em prototipagem de empacotamento — ver ROADMAP),
  NVIDIA NIM com preset Riva Translate (Fase 5 — reutiliza `OpenAICompatibleProvider` por baixo, já que
  o endpoint é compatível com OpenAI; falta só a validação do formato de prompt real com uma API key),
  DeepL, Google, OpenAI, Gemini, Claude (Fase 6). Cada um isolado o suficiente para uma falha ou
  mudança de API de um não afetar os outros nem o core do app (seção 18).

### TS4Translator.Persistence

SQLite via EF Core, schema versionado por migrations reais (DEC-007). Implementado até agora:
`TranslationMemory` (busca exata por hash SHA-256 do texto normalizado + par de locales — a
"killer feature" da seção 63) e `Project` (nome, arquivos `.package` importados, par de locales,
timestamps — seção 33). `TranslatorDbContext` é resolvido via `IDbContextFactory<TranslatorDbContext>`,
nunca injetado diretamente, porque um app desktop não tem escopo de requisição como o ASP.NET.
`ServiceCollectionExtensions.AddPersistenceServices(databasePath)` registra a factory,
`ITranslationMemory` e `IProjectService`. Listagens ordenadas por `DateTimeOffset` (ex.: "projetos
recentes") são ordenadas do lado do cliente, não em SQL — o provider SQLite do EF Core não traduz
`ORDER BY` sobre colunas `DateTimeOffset` (DEC-008, descoberto por um teste de integração real
falhando, não por leitura de documentação). As demais entidades da seção 37 (StringEntry,
GlossaryTerm, TranslationJob, etc.) entram como migrations incrementais, uma por feature, quando essa
feature for implementada — não antecipadamente. API keys **nunca** ficam aqui em texto puro — isso é
responsabilidade de `Infrastructure` via Windows Credential Manager/DPAPI (seção 19, ainda não
implementado).

### TS4Translator.Infrastructure

`AppDataPaths` resolve e cria `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}` (seção 48) —
usado pelo `App` para apontar o SQLite e, futuramente, logs/modelos Argos. Logging estruturado e
armazenamento seguro de credenciais (seção 19) ainda não implementados.

### TS4Translator.App

Gerado a partir do template `dotnet new winui-navview` (DEC-005), com o namespace corrigido de
`TS4Translator_App` para `TS4Translator.App`. O composition root (`App.xaml.cs`) monta um
`ServiceCollection` simples (mesmo padrão do `SimsPackageBatcher`, não um `Generic Host` completo —
ver DEC-007) chamando `AddSims4Services()`, `AddTranslationServices()`, `AddPersistenceServices(...)`,
registra `IPathPickerService`/`HomePageViewModel` locais ao projeto App, e roda `Database.Migrate()`
uma vez antes de ativar a janela. `App.Services` (um service locator estático — DEC-009) e
`App.WindowHandle` ficam disponíveis para páginas e serviços que precisam deles.

- **Home** (`Pages/HomePage.xaml` + `ViewModels/HomePageViewModel.cs`) — a primeira tela de produto
  real: botão "Importar packages" (via `IPathPickerService`/`FileOpenPicker`, seção 34), cria um
  `TranslationProject` a partir dos arquivos escolhidos (locale fixo `en-US`→`pt-BR` por enquanto —
  seletor de locale ainda não existe), mostra a contagem de tabelas STBL encontradas via
  `IPackageService`, e lista projetos recentes via `IProjectService`.
- **About**/**Settings** — ainda os placeholders do template, sem lógica.

**Nunca executado interativamente** — sem acesso a um desktop Windows nesta sessão de
desenvolvimento, a validação parou em "`dotnet build` compila e o compilador XAML aceita os `x:Bind`".
Nenhum clique real, nenhuma verificação visual de Mica/tema, nenhum teste do file picker de verdade.
Ver `docs/HANDOFF.md` para o que uma sessão com desktop Windows deveria confirmar primeiro.

## Convenções de qualidade

- `Directory.Build.props` na raiz aplica `TreatWarningsAsErrors`, `EnableNETAnalyzers`,
  `AnalysisLevel=latest-recommended` a todo o repositório.
- `tests/Directory.Build.props` importa o da raiz e adiciona `NoWarn=CA1707` só para projetos de
  teste, para permitir a convenção `Metodo_Cenario_Resultado` em nomes de teste (DEC-003).
- Toda decisão não-trivial (troca de biblioteca, formato de arquivo, contrato de interface que afeta
  múltiplas camadas) é registrada em `DECISIONS.md` antes de seguir em frente.
