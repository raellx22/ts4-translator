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
- `Packages/`, `Translation/` (provider/job contracts), `Memory/`, `Projects/` — contratos e modelos das
  camadas correspondentes.
- `Glossary/` — `GlossaryTerm` (record com `AppearsIn(text)`, usado pelo `TranslationOrchestrator` para
  filtrar quais termos são relevantes a um batch específico) e `IGlossaryService`. Ver DEC-012.
- (Planejado, ainda não criado) `TranslationJob`/estados de job (seção 35) — será adicionado quando
  retry/pause/resume for implementado, não antecipadamente (seção 60 do brief: evitar abstração antes
  de haver uma implementação real para validá-la).

### TS4Translator.Translation

Implementações que não dependem de nenhum SDK de terceiros nem de I/O:

- `TokenProtector : ITokenProtector` — extração por regex de tokens `{...}`, tags `<...>` e quebras
  de linha; reconciliação por identidade de placeholder. Ver DEC-004.
- `TranslationValidator : ITranslationValidator` — funde os problemas do `TokenRestoreResult` com
  checagens próprias (vazio, idêntico ao original, discrepância de tamanho, markup desbalanceado).
- `BatchBuilder : IBatchBuilder` — agrupa itens por contagem e por total de caracteres; um item maior
  que o limite sozinho vira seu próprio batch em vez de ser cortado.
- `TranslationOrchestrator : ITranslationOrchestrator` — o pipeline completo da seção 10/11: consulta
  `ITranslationMemory.FindExactAsync` primeiro (pula o provider quando já existe tradução exata); se
  não houver hit exato, tenta `FindSimilarAsync` com um limiar conservador de 97% (`StringSimilarity`,
  Levenshtein) — uma correção pequena de texto no mod reaproveita a tradução anterior sem chamar o
  provider, mas sempre volta marcada `ValidationIssueCode.FuzzyMatchReused` para revisão humana, nunca
  como sucesso silencioso (seção 26/63, DEC-016); para o restante (sem hit exato nem fuzzy),
  agrupa em batches, protege tokens, chama o provider, **reconcilia cada resultado pelo `Id` do item**
  (nunca por posição/contagem — se o provider omitir ou duplicar um `Id`, o item vira uma falha
  explícita com `ValidationIssueCode.ProviderResultMissing`/`ProviderResultDuplicated`, nunca um
  deslocamento silencioso de traduções), restaura tokens, valida, e só grava na translation memory os
  itens sem problema crítico. `TranslateAsync` aceita um `glossaryTerms` opcional (seção 23):
  `TranslationOrchestrator` filtra, **por batch**, só os termos cujo `GlossaryTerm.AppearsIn(text)`
  bate com algum item daquele batch específico, antes de repassar ao provider — nunca o glossário
  inteiro de uma vez. Ver DEC-012.

Translation Profiles (seção 22) existem no escopo de "instruções livres por projeto", não uma
biblioteca de presets nomeados: `TranslateAsync` aceita um `customInstructions` opcional
(`TranslationProject.CustomInstructions`, editável na `EditorPage`), repassado a cada batch sem
filtragem (diferente do glossário, não depende do conteúdo do batch). `TranslationMemoryEntry.ProfileName`
continua `null` — não há um nome de perfil reutilizável para gravar. Ver DEC-015.

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
  travam o batch inteiro. Quando `glossaryTerms` traz itens relevantes (`Capabilities.SupportsGlossary: true`),
  `BuildSystemPrompt` acrescenta uma seção listando cada termo (`NeverTranslate` vira "nunca traduzir";
  os demais viram "deve ser traduzido como X") — ver DEC-012. 15 testes com um `HttpMessageHandler`
  falso (nenhum servidor real é necessário nem contatado — seção 44).
- `OpenAICompatible.OpenAICompatibleProviderFactory : ITranslationProviderFactory` — constrói o
  provider ativo a partir do que está salvo em `IProviderConfigurationService`; a UI (`EditorPage`,
  `SettingsPage`) só conhece essa fábrica, nunca `OpenAICompatibleProvider` diretamente.
- Ainda faltam: Argos Translate (Fase 4, bloqueado em prototipagem de empacotamento — ver ROADMAP),
  NVIDIA NIM com preset Riva Translate (Fase 5 — reutiliza `OpenAICompatibleProvider` por baixo, já que
  o endpoint é compatível com OpenAI; falta só a validação do formato de prompt real com uma API key),
  DeepL, Google, OpenAI, Gemini, Claude (Fase 6). Cada um isolado o suficiente para uma falha ou
  mudança de API de um não afetar os outros nem o core do app (seção 18).

### TS4Translator.Persistence

SQLite via EF Core, schema versionado por migrations reais (DEC-007). Implementado até agora:
`TranslationMemory` (busca exata por hash SHA-256 do texto normalizado + par de locales, mais busca
fuzzy por similaridade de Levenshtein via `FindSimilarAsync` — a "killer feature" da seção 63, ver
DEC-016), `Project` (nome, arquivos `.package` importados, par de locales,
timestamps — seção 33), `ProjectString` (cada string extraída, ligada ao `Project` e à `StblKey`/
`KeyHash` de origem — upsert por hash preserva traduções ao reimportar a mesma versão de um mod,
seção 25), `ProviderConfiguration` (endpoint/modelo/temperatura do provider ativo; a API key em si
não mora aqui — ver `Infrastructure`), `ProjectStringRevision` (texto anterior de uma tradução
sobrescrita, gravado por `EfProjectStringService.UpdateTranslationAsync` antes de qualquer overwrite —
não na primeira tradução de uma string, seção 31), e `GlossaryTerm` (global quando `ProjectId == null`,
específico de projeto quando não — DEC-012). `TranslatorDbContext` é resolvido via
`IDbContextFactory<TranslatorDbContext>`, nunca injetado diretamente, porque um app desktop não tem
escopo de requisição como o ASP.NET. `ServiceCollectionExtensions.AddPersistenceServices(databasePath)`
registra a factory e todos os serviços acima. Listagens ordenadas por `DateTimeOffset` (ex.: "projetos
recentes", histórico de revisões) são ordenadas do lado do cliente, não em SQL — o provider SQLite do
EF Core não traduz `ORDER BY` sobre colunas `DateTimeOffset` (DEC-008, descoberto por um teste de
integração real falhando, não por leitura de documentação).
`EfProjectStringService.ReplaceStringsAsync` retorna um `ImportDiffResult` (novo/alterado/inalterado/
removido) em vez de `void` — uma reimportação cujo texto de origem mudou numa entrada já traduzida
marca a entrada `NeedsReview` sem descartar a tradução existente (DEC-012). A entidade `TranslationJob`
da seção 37 entra como migration incremental quando retry/pause/resume for implementado — não
antecipadamente.

### TS4Translator.Infrastructure

`AppDataPaths` resolve e cria `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}` (seção 48) —
usado pelo `App` para apontar o SQLite e, futuramente, logs/modelos Argos. `DpapiSecretStore :
ISecretStore` guarda API keys criptografadas via DPAPI do Windows (`CurrentUser` scope) em
`%LOCALAPPDATA%\TS4Translator\Data\Secrets`, nunca em texto puro nem no SQLite (seção 19) —
`IProviderConfigurationService` (em `Persistence`) delega a ele através da interface, sem referenciar
o assembly `Infrastructure` diretamente. Logging estruturado ainda não implementado.

### TS4Translator.App

Gerado a partir do template `dotnet new winui-navview` (DEC-005), com o namespace corrigido de
`TS4Translator_App` para `TS4Translator.App`. O composition root (`App.xaml.cs`) monta um
`ServiceCollection` simples (mesmo padrão do `SimsPackageBatcher`, não um `Generic Host` completo —
ver DEC-007) chamando `AddSims4Services()`, `AddTranslationServices()`, `AddPersistenceServices(...)`,
registra `IPathPickerService`/`HomePageViewModel` locais ao projeto App, e roda `Database.Migrate()`
uma vez antes de ativar a janela. `App.Services` (um service locator estático — DEC-009) e
`App.WindowHandle` ficam disponíveis para páginas e serviços que precisam deles.

- **Home** (`Pages/HomePage.xaml` + `ViewModels/HomePageViewModel.cs`) — importar `.package` via
  `IPathPickerService`/`FileOpenPicker` **ou** arrastando arquivos para a janela (`AllowDrop`/
  `Page_DragOver`/`Page_Drop`, seção 34) — os dois caminhos convergem em `RunImportAsync`, cria um
  `TranslationProject` a partir dos arquivos escolhidos (locale fixo `en-US`→`pt-BR` por enquanto —
  seletor de locale ainda não existe), extrai e persiste **cada string de cada STBL encontrada** via
  `IProjectStringService` (não só a contagem) e mostra o `ImportDiffResult` de cada import/reimport
  (quantas novas/alteradas/reaproveitadas/removidas — DEC-012), e lista projetos recentes via
  `IProjectService`. Clicar num projeto navega para o Editor (`Frame.Navigate(typeof(EditorPage), project.Id)`).
- **Editor** (`Pages/EditorPage.xaml` + `ViewModels/EditorPageViewModel.cs`) — lista as strings de um
  projeto (`ListView` virtualizada, original + tradução editável + status por linha), com filtro por
  status e busca por texto sobre uma lista mestra em memória; "Traduzir pendentes" roda
  `ITranslationOrchestrator` (buscando o glossário aplicável via `IGlossaryService` e as instruções de
  estilo do projeto - DEC-015 - antes de cada chamada) contra o provider ativo
  (`ITranslationProviderFactory`); processa as strings pendentes em blocos de 100
  (`EditorPageViewModel.TranslationChunkSize`), persistindo cada bloco antes do próximo - uma tradução
  de milhares de strings mostra progresso real (`ProgressBar`) e pode ser cancelada a qualquer momento
  sem perder o que já foi salvo (DEC-018); editar a tradução manualmente salva no `LostFocus`
  (`UpdateTranslationAsync(..., ManuallyEdited, ...)`, gravando a versão anterior em
  `ProjectStringRevision` — DEC-012); "Exportar" agrupa por STBL de origem, troca o locale via
  `IStringTableService.WithLocale`, escreve com `IPackageService.WriteStringTablesAsync`, e só reporta
  sucesso depois de reabrir o arquivo e comparar cada string com o que deveria estar lá (round-trip
  real, seção 28).
- **Settings** (`Pages/SettingsPage.xaml` + `ViewModels/SettingsPageViewModel.cs`) — formulário para
  configurar o provider OpenAI-compatible ativo (nome/endpoint/modelo/API key) via
  `IProviderConfigurationService`, com botão "Testar conexão" que chama o provider de verdade, e um
  botão "Usar preset NVIDIA NIM" que só preenche endpoint/model conhecidos (DEC-014).
- **Glossário** (`Pages/GlossaryPage.xaml` + `ViewModels/GlossaryPageViewModel.cs`) — adicionar/listar/
  remover termos **globais** do glossário via `IGlossaryService` (termos por projeto ainda não têm UI —
  DEC-013). O botão "Remover" de cada linha usa `Tag="{x:Bind}"` + `Click` no code-behind, não
  `Binding ElementName=...Command` — essa segunda forma não resolveu de forma confiável dentro do
  `ItemTemplate` de uma `ListView` (bug real encontrado rodando o app, corrigido — ver DEC-013).
- **About** — ainda o placeholder do template, sem lógica.

**Executado e clicado de verdade, fluxo completo confirmado** (DEC-010) — Mica, tema, navegação, e a
importação de ponta a ponta (seletor de arquivo → contagem de STBL → criação do projeto → lista de
recentes) confirmados com screenshots reais. A única pegadinha: testar isso exige rodar o app como um
pacote MSIX instalado, não via `dotnet run` (que falha ao ativar o seletor de arquivos) — ver
`docs/BUILD.md` para o procedimento.

## Convenções de qualidade

- `Directory.Build.props` na raiz aplica `TreatWarningsAsErrors`, `EnableNETAnalyzers`,
  `AnalysisLevel=latest-recommended` a todo o repositório.
- `tests/Directory.Build.props` importa o da raiz e adiciona `NoWarn=CA1707` só para projetos de
  teste, para permitir a convenção `Metodo_Cenario_Resultado` em nomes de teste (DEC-003).
- Toda decisão não-trivial (troca de biblioteca, formato de arquivo, contrato de interface que afeta
  múltiplas camadas) é registrada em `DECISIONS.md` antes de seguir em frente.
