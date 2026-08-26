# Decisions

Architectural decisions for TS4 Translator, in the format requested by the project brief. Only
decisions with real consequences are recorded here — trivial choices are not.

---

## DEC-001 — Reuse the stack proven by SimsPackageBatcher

**Contexto:** O projeto exige WinUI 3 + Windows App SDK, .NET moderno, e uma biblioteca madura para
DBPF/STBL do The Sims 4. Uma sessão anterior neste mesmo ambiente Windows já construiu e testou até
o fim `SimsPackageBatcher` (`../SimsPackageBatcher/`), um app WinUI 3 que lê/escreve `.package` via
LlamaLogic.Packages, com build e testes passando em 2026-08-26.

**Decisão:** Adotar exatamente o mesmo conjunto de versões, já que está comprovadamente funcional
neste ambiente, em vez de redescobrir compatibilidade do zero:

| Componente | Versão |
|---|---|
| .NET SDK | 10.0.400 (`global.json`, `rollForward: latestPatch`) |
| TFM da App | `net10.0-windows10.0.26100.0` |
| `TargetPlatformMinVersion` | `10.0.17763.0` (Windows 10 1809) |
| Windows App SDK | 2.4.0 |
| LlamaLogic.Packages | 3.8.2 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.DependencyInjection | 10.0.11 |
| xUnit | 2.9.3 / Microsoft.NET.Test.Sdk 17.14.1 |

**Alternativas:** Usar a versão mínima do Windows App SDK sugerida pelo template `dotnet new`
(1.8.x) e a versão mais recente do LlamaLogic.Packages disponível no momento.

**Motivo:** Zero incerteza de compatibilidade — o par exato dessas versões já compilou, empacotou
em MSIX e passou por testes de integração reais neste mesmo ambiente. Divergir sem motivo introduziria
uma variável de risco sem benefício correspondente.

**Consequências:** Ao atualizar qualquer uma dessas versões no futuro, validar contra a suíte de
testes deste projeto e, se possível, também contra a do `SimsPackageBatcher`.

---

## DEC-002 — Formato de solução `.sln` clássico, não `.slnx`

**Contexto:** O SDK 10.0.400 tem `dotnet new sln` gerando `.slnx` (XML) por padrão. Ao compilar
`TS4Translator.slnx` com `-p:Platform=x64`, o MSBuild falhou com `MSB4126` (configuração de solução
inválida) — o `.slnx` gerado não carrega o mapeamento de configuração/plataforma que a mistura de
class libraries `AnyCPU` com o projeto WinUI (`x86;x64;ARM64`) exige.

**Decisão:** Gerar a solução com `dotnet new sln --format sln` (formato clássico), igual ao que
`SimsPackageBatcher.sln` já usa com sucesso.

**Alternativas:** Investigar e escrever manualmente as seções `GlobalSection(SolutionConfigurationPlatforms)`
equivalentes dentro do `.slnx`.

**Motivo:** O formato clássico resolveu o build imediatamente sem configuração manual adicional, e
mantém consistência com o projeto irmão.

**Consequências:** Nenhuma perda de funcionalidade percebida; o `.slnx` pode ser reavaliado se uma
versão futura do SDK corrigir esse gap.

---

## DEC-003 — CA1707 suprimido apenas em projetos de teste

**Contexto:** `Directory.Build.props` define `TreatWarningsAsErrors=true` com `AnalysisLevel=latest-recommended`,
o que inclui CA1707 (proíbe underscores em identificadores). A convenção `MetodoTestado_Cenario_ResultadoEsperado`
para nomes de teste xUnit — mais legível que PascalCase concatenado — viola essa regra.

**Decisão:** Criar `tests/Directory.Build.props` que importa o `Directory.Build.props` raiz e adiciona
`CA1707` a `NoWarn`, escopado apenas à árvore `tests/`.

**Alternativas:** (a) Nomear testes sem underscore (ex.: `RestoreMissingTokenIsInvalid`), como o
`SimsPackageBatcher` fez; (b) suprimir CA1707 globalmente.

**Motivo:** A convenção com underscore comunica cenário e resultado esperado de forma muito mais
clara em uma suíte que crescerá para centenas de casos (tokens, STBL, providers, validação). Suprimir
apenas em `tests/` mantém o código de produção sob a regra normal.

**Consequências:** Nenhuma — código de produção continua proibido de usar underscores em identificadores.

---

## DEC-004 — TokenProtector: extração por regex, reconciliação por identidade de placeholder

**Contexto:** Seção 9 do brief exige que nenhum provider de tradução seja responsável por preservar
tokens como `{0.SimFirstName}`, `{M0.He}`, `{F0.She}`, tags de markup e quebras de linha.

**Decisão:**
- Um único regex de extração cobre três categorias na mesma passada, preservando ordem de aparição:
  chaves `\{[^{}\r\n]*\}` (não aninhadas), tags `<\/?[a-zA-Z][^<>\r\n]*\/?>`, e quebras de linha
  literais (`\r\n`, `\n`, `\r`).
- Cada ocorrência recebe um placeholder opaco e sequencial (`__TS4_TOKEN_0001__`, `__TS4_TOKEN_0002__`, ...),
  mesmo quando o texto original do token se repete — isso evita ambiguidade ao reconciliar depois.
- `Restore` nunca confia em ordem ou contagem: localiza cada placeholder esperado pelo texto exato,
  substitui de volta ao valor original, e reporta como problema qualquer ausência, duplicação, token
  inesperado (não reconhecido), ou placeholder alterado o suficiente para não bater com nenhum esperado.
- Reordenação de tokens (todos presentes, uma vez cada, em ordem diferente) é sinalizada mas **não**
  marca o resultado como inválido — ordem de palavras varia legitimamente entre idiomas.

**Alternativas:** Reconciliar por posição/índice sequencial (frágil se o provider reordenar);
usar hash em vez de índice sequencial no placeholder (não traz benefício e dificulta debug visual).

**Motivo:** Corresponde exatamente ao fluxo descrito na seção 9, e o design por identidade (não por
posição) é o que permite detectar corretamente duplicação, ausência e reordenação como categorias
distintas, conforme a seção 9 exige.

**Consequências:** Tags `<...>` genéricas podem, em teoria, capturar texto que não é markup real do
Sims 4 (por exemplo, um "menor que" literal seguido de um "maior que"). Isso ainda não foi confirmado
como um problema real — nenhuma amostra real de STBL foi inspecionada nesta sessão. Repriorizar se
aparecer em testes com mods reais.

---

## DEC-005 — App WinUI iniciado a partir do template `winui-navview`

**Contexto:** O SDK tem o template `dotnet new winui-navview` instalado, que já gera uma shell
completa com `NavigationView`, título estendido (`ExtendsContentIntoTitleBar`), páginas Home/About/Settings
e empacotamento MSIX de projeto único — exatamente a estrutura pedida nas seções 2 e 41.

**Decisão:** Gerar `TS4Translator.App` com `dotnet new winui-navview -tfm net10.0 -tpmv 10.0.17763.0 -w 2.4.0`,
depois renomear o namespace gerado (`TS4Translator_App`, com underscore por sanitização automática do
nome do projeto) para `TS4Translator.App`.

**Alternativas:** Criar a shell manualmente a partir do zero, ou partir do template mais simples
`winui-mvvm` (sem `NavigationView` pronto).

**Motivo:** Menos código de infraestrutura para escrever e validar manualmente; o template já resolve
Mica/tema via `ExtendsContentIntoTitleBar`, e a página Settings já existe como ponto de partida para
a seção 41.

**Consequências:** Nenhuma perda de controle arquitetural — o template gera apenas XAML/code-behind
de shell, sem lógica de negócio; DI, MVVM com CommunityToolkit.Mvvm, e os serviços da aplicação foram
adicionados por cima, não vieram do template.

---

## DEC-006 — Locale de STBL via `LlamaLogic.Packages.SmartSimUtilities`, não reimplementado à mão

**Contexto:** A seção 8 do brief exige confirmar como o locale é codificado na Instance ID de um
recurso STBL do Sims 4, sem confiar em fontes secundárias. Uma pesquisa inicial (agente em background)
encontrou um enum interno `LocaleFullInstancePrefix` no código-fonte do LlamaLogic (o byte alto da
Instance ID mapeia locale → byte, ex.: `en_US=0x00`, `pt_BR=0x11`), mas esse tipo não está documentado
no XML de API pública do pacote instalado — inspecionando `LlamaLogic.Packages.xml` (3.8.2) diretamente,
`LocaleFullInstancePrefix` não aparece nenhuma vez, o que indica que é `internal`.

O que **é** público e foi confirmado por inspeção direta do XML doc do pacote instalado (não apenas
pela pesquisa) é `LlamaLogic.Packages.SmartSimUtilities`, com três métodos estáticos:

- `GetStringTableResourceKey(CultureInfo locale, uint group, ulong fullInstance) -> ResourceKey`
- `GetResourceKeyForLocale(ResourceKey key, CultureInfo locale) -> ResourceKey`
- `GetStringTableLocale(ResourceKey key) -> CultureInfo`

**Decisão:** `TS4Translator.Sims4.SmartSimStringTableService` chama esses três métodos diretamente,
em vez de reimplementar o deslocamento de bits do byte de locale. `IStringTableService` (em `Core`)
expõe `CultureInfo` do BCL como o tipo de locale — não um enum próprio nem o enum interno do LlamaLogic
— porque `CultureInfo` já é o vocabulário natural de locale em .NET e evita duplicar a tabela de
mapeamento do LlamaLogic em outro lugar do código.

**Verificado empiricamente** (11 testes em `SmartSimStringTableServiceTests`/`Sims4PackageServiceTests`,
10 aprovados de primeira, 1 corrigido após falha real):
- `CultureInfo.GetCultureInfo("en-US")`, `"pt-BR"` e `"fr-FR"` funcionam como esperado com
  `GetStringTableResourceKey`/`GetStringTableLocale` — confirma que a biblioteca aceita os nomes de
  cultura padrão do BCL (com hífen), não os identificadores com underscore do enum interno.
- **A doc XML do próprio LlamaLogic 3.8.2 está incorreta**: declara `<exception cref="ArgumentOutOfRangeException">`
  para locale não suportado, mas o binário na verdade lança `System.NotSupportedException`
  ("Unknown or unsupported locale: ar-SA") — confirmado rodando o teste, não assumido. `IStringTableService`
  documenta `NotSupportedException`, refletindo o comportamento real, não a doc do pacote.
- `WithLocale` aplicado ao mesmo locale já presente é idempotente (mesma chave de volta).
- Uma chave PT-BR criada, escrita em um package sintético via `Sims4PackageService`, reaberta e
  relida via `FindStringTablesAsync`, continua identificável como PT-BR por `GetLocale`.

**Alternativas:** Copiar a tabela `LocaleFullInstancePrefix` (en_US=0x00 ... pt_BR=0x11, sem Thai)
para dentro do projeto e fazer o deslocamento de bits manualmente.

**Motivo:** Zero risco de divergência de uma futura versão da biblioteca (se o LlamaLogic mudar a
codificação de locale internamente, `SmartSimUtilities` continua correto sem exigir mudança aqui).
Menos código próprio para testar e manter.

**Consequências:** `TS4Translator.Sims4` depende de `SmartSimUtilities` continuar existindo com essa
assinatura em versões futuras do LlamaLogic.Packages — revalidar com testes ao atualizar a versão do
pacote (ver DEC-001).

---

## DEC-007 — Persistência: EF Core + SQLite via `IDbContextFactory`, migrations reais

**Contexto:** A seção 37 exige SQLite com schema versionado por migrations. `Microsoft.EntityFrameworkCore.Sqlite`
10.0.0 (a versão inicial visada) falhou a restauração: o NuGet audit tratou como erro duas
vulnerabilidades conhecidas de alta severidade nas dependências transitivas (`SQLitePCLRaw.lib.e_sqlite3`
2.1.11 e `System.Security.Cryptography.Xml` 9.0.0).

**Decisão:**
- Usar `Microsoft.EntityFrameworkCore.Sqlite`/`.Design` **10.0.11** (último patch da série 10.x no
  momento), que resolve para uma versão corrigida de `SQLitePCLRaw.lib.e_sqlite3` sem nenhum aviso de
  segurança — mesma versão de patch já usada para `Microsoft.Extensions.DependencyInjection` (DEC-001),
  então não introduz uma nova linha de versão para acompanhar.
- Resolver `TranslatorDbContext` via `IDbContextFactory<TranslatorDbContext>` (`AddDbContextFactory`),
  não via `AddDbContext` com escopo por requisição — um app desktop WinUI não tem limites de escopo
  como o ASP.NET; a factory cria um `DbContext` novo por operação, evitando problemas de concorrência
  em um `DbContext` de vida singleton.
- Migrations reais via `dotnet ef migrations add` (ferramenta `dotnet-ef` instalada globalmente nesta
  sessão), com `TranslatorDbContextFactory : IDesignTimeDbContextFactory<TranslatorDbContext>` só para
  esse fluxo de design-time — em runtime o `DbContext` só é resolvido pela injeção de dependência.
- CA1861 (`Directory.Build.props` → `TreatWarningsAsErrors`) dispara no código gerado pela própria
  ferramenta de migrations do EF Core (literais de array em `Up`/`Down`). Suprimido em todo o projeto
  `TS4Translator.Persistence` via `NoWarn`, porque o arquivo é regenerado a cada `migrations add` — não
  faz sentido corrigi-lo manualmente para ser sobrescrito na próxima migration.
- `SqliteConnection.ClearAllPools()` é necessário no cleanup dos testes de integração antes de apagar
  o diretório temporário — o pool de conexões nativo do `Microsoft.Data.Sqlite` mantém o arquivo aberto
  mesmo depois do `DbContext`/`ServiceProvider` serem descartados.

**Alternativas:** `Microsoft.Data.Sqlite` puro sem EF Core (menos abstração, mas sem migrations
automáticas — teria que escrever DDL/versionamento à mão); Dapper.

**Motivo:** EF Core dá migrations versionadas de graça (requisito explícito da seção 37), e o padrão
`IDbContextFactory` é a recomendação oficial da própria Microsoft para apps desktop/não-ASP.NET.

**Consequências:** Toda entidade de persistência futura (Project, StringEntry, GlossaryTerm,
TranslationJob, etc. — seção 37) deve ser adicionada como uma nova migration incremental, nunca editando
`20260826172805_InitialCreate` depois que ela existir em qualquer branch compartilhado.

---

## DEC-008 — Ordenação por `DateTimeOffset` no SQLite: client-side, não em SQL

**Contexto:** `EfProjectService.ListRecentAsync` originalmente fazia `OrderByDescending(p => p.UpdatedAtUtc).Take(maxCount)`
diretamente na query EF. Um teste de integração real (`ListRecentAsync_ReturnsMostRecentlyUpdatedFirst`)
falhou imediatamente com `NotSupportedException: SQLite does not support expressions of type
'DateTimeOffset' in ORDER BY clauses` — o provider `Microsoft.EntityFrameworkCore.Sqlite` (10.0.11)
simplesmente não traduz `ORDER BY` sobre uma coluna `DateTimeOffset` para SQL, mesmo a coluna sendo
armazenada como TEXT ISO-8601 (que ordenaria corretamente por comparação lexicográfica se o SQL
chegasse a ser gerado).

**Decisão:** Buscar todos os registros do projeto com `.Include(...).ToListAsync()`, depois ordenar e
paginar (`OrderByDescending`/`Take`) do lado do cliente, em LINQ-to-Objects.

**Alternativas:** Adicionar uma coluna `long UpdatedAtUtcTicks` só para ordenação (mais uma coluna
redundante para manter sincronizada em todo `SaveAsync`/`UpdateAsync`); usar SQL bruto via
`FromSqlInterpolated`.

**Motivo:** Este é exatamente o tipo de fato que não deve ser assumido de documentação — só foi
descoberto porque havia um teste de integração real rodando contra SQLite real, não um provedor
in-memory. Ordenação client-side é aceitável na escala real de "projetos recentes" (dezenas, não
milhões, de linhas) e evita uma coluna derivada.

**Consequências:** Qualquer query futura que precise ordenar por uma coluna `DateTimeOffset` no SQLite
via EF Core tem o mesmo problema — sempre escrever um teste de integração real (não apenas um teste
com um repositório fake) antes de confiar em uma ordenação por data/hora funcionar em produção.

---

## DEC-009 — Primeira tela real: ViewModel por service locator estático, `[ObservableProperty]` em propriedades parciais

**Contexto:** A primeira tela de produto (`HomePage`: importar `.package`, criar `TranslationProject`,
listar projetos recentes) precisa de um `HomePageViewModel` injetado com `IProjectService`,
`IPackageService` e um serviço de file picker. Mas a shell gerada pelo template `winui-navview`
navega com `NavFrame.Navigate(typeof(HomePage))` — a API `Frame.Navigate(Type)` do WinUI instancia a
página via construtor sem parâmetros; não existe um jeito nativo de injetar dependências através dela.

**Decisão:**
- `App` expõe `public static IServiceProvider Services { get; }`, populado no construtor logo após
  `BuildServiceProvider()`. Cada página resolve seu próprio ViewModel no construtor:
  `ViewModel = App.Services.GetRequiredService<HomePageViewModel>();` antes de `InitializeComponent()`.
  Isso é um service locator, não injeção de construtor "de verdade" — aceitável aqui porque é a única
  alternativa razoável sem reescrever a navegação para passar parâmetros (`Frame.Navigate(Type, object)`
  com um parâmetro por página seria mais estrutura para o mesmo resultado, neste estágio).
- `HomePageViewModel` é registrado `Transient` (uma instância nova por navegação até a página, não
  compartilhada) — os demais serviços de domínio que ele consome continuam `Singleton`.
- `[ObservableProperty]` do CommunityToolkit.Mvvm 8.4.2 sobre um **campo** privado (`private bool
  isBusy;`) falhou o build com `MVVMTK0045`: em apps WinUI 3/WinRT, o gerador de código do toolkit
  exige a sintaxe de **propriedade parcial** (`[ObservableProperty] public partial bool IsBusy { get; set; }`,
  recurso do C# 13) em vez de campo — a sintaxe antiga não produz o marshalling WinRT correto.
  `HomePageViewModel` usa a sintaxe de propriedade parcial em toda propriedade observável.

**Alternativas:** Passar o ViewModel como parâmetro de `Frame.Navigate` (mais correto a longo prazo,
mais código de navegação para escrever agora); usar `CommunityToolkit.Mvvm`'s `WeakReferenceMessenger`
ou um framework de navegação de terceiros.

**Motivo:** Menor caminho até uma tela funcional e testável por `dotnet build`, sem introduzir uma
camada de navegação própria antes de haver mais de duas páginas reais que precisem dela.

**Consequências:** Se o número de páginas com ViewModels crescer o suficiente para o service locator
ficar repetitivo ou para passar estado entre páginas via navegação se tornar necessário, revisar para
`Frame.Navigate(Type, object)` com um parâmetro de navegação tipado por página, ou um `INavigationService`
próprio — não adicionar isso preventivamente enquanto só uma página precisa de DI.

---

## DEC-010 — App rodado e clicado de verdade pela primeira vez; achado real: `AsyncRelayCommand` engole exceções, e o FileOpenPicker falha com E_FAIL neste ambiente

**Contexto:** Depois de escrever a `HomePage` sem nenhuma verificação visual (nenhuma sessão anterior
tinha acesso a um desktop Windows), esta sessão descobriu que o ambiente de execução **é** um desktop
Windows 11 real do usuário — `dotnet run --project ... -p:Platform=x64` de fato lança a janela do app
de verdade, e é possível interagir com ela via Windows UI Automation (`System.Windows.Automation`) e
capturar screenshots via `System.Drawing`/`CopyFromScreen`. Isso permitiu, pela primeira vez,
verificação real da UI em vez de só `dotnet build`.

**O que foi confirmado funcionando, com screenshot real:**
- A janela abre, com Mica visível, tema escuro do sistema aplicado corretamente.
- `NavigationView` com Home/About/Settings, título "TS4 Translator" correto.
- Navegação entre Home → About funciona (botão voltar aparece, conteúdo troca, seleção do menu
  atualiza) — testado via `SelectionItemPattern.Select()` do UI Automation.
- `HomePage` renderiza corretamente: título, botão "Importar packages", texto de status, lista de
  projetos recentes vazia (banco novo).

**Bug real encontrado e corrigido:** `HomePageViewModel.ImportPackagesAsync` chamava
`_pathPicker.PickPackageFilesAsync()` **fora** do bloco `try/catch`. `AsyncRelayCommand` do
CommunityToolkit.Mvvm, no construtor usado (`new AsyncRelayCommand(Func<Task>)`), não relança
exceções do delegate na UI thread por padrão — uma falha ali ficava com a `Task` interna em estado
`Faulted` silenciosamente, o comando "clicava" sem fazer nada visível, sem crash, sem mensagem.
Confirmado ao clicar o botão de verdade (via `InvokePattern` e depois via clique de mouse sintético
real com `SendInput`/`mouse_event`) e ver que nada acontecia. Corrigido movendo a chamada do picker
para dentro do `try`. Esse bug existiria para qualquer usuário real, não é um artefato do ambiente de
teste — **corrigido e commitado**.

**Achado não resolvido, documentado para a próxima sessão:** mesmo depois da correção acima, clicar
"Importar packages" (tanto via automação de acessibilidade quanto via clique de mouse sintético real,
`SendInput`) produz consistentemente:

```
System.Runtime.InteropServices.COMException (0x80004005): E_FAIL
```

lançada de dentro de `FileOpenPicker.PickMultipleFilesAsync()`, sem mensagem nem inner exception.
Pesquisa (busca na web) encontrou múltiplos relatos do WindowsAppSDK com exatamente esse HRESULT
associado a **apps rodando em contexto elevado (Administrador)** sem a capability `allowElevation`/
`requireAdministrator` corretamente declarada — e a sessão de ferramentas usada neste ambiente
(PowerShell/Bash) **roda como Administrador** (confirmado via token de elevação: `TokenElevation = 1`).
Uma tentativa de des-elevar o processo filho via `Shell.Application.ShellExecute` (técnica comum para
COM de shell) **não funcionou** — o processo lançado continuou com `TokenElevation = 1`, então a
hipótese de elevação **não foi comprovada nem descartada**, só ficou como a explicação mais provável
sem confirmação definitiva.

**Decisão:** Não tentar "consertar" o picker sem antes confirmar a causa real com um teste limpo:
rodar o app publicado/instalado (não via `dotnet run` elevado) em uma sessão de usuário padrão,
sem privilégios de administrador. Documentar isso como bloqueio de verificação, não como bug
confirmado do código do app.

**Alternativas não tentadas:** declarar `allowElevation` no `Package.appxmanifest` (só faz sentido se
a hipótese de elevação for confirmada primeiro — adicionar essa capability sem necessidade real seria
over-engineering); trocar `FileOpenPicker` por um diálogo Win32 clássico via interop (perderia a
integração nativa com o Explorer que a seção 2 do brief pede).

**Consequências:** Até essa verificação acontecer, **não considere `HomePage` funcionalmente completa
mesmo que compile** — o fluxo de importação nunca foi visto terminando com sucesso, só o de erro. Ver
`docs/STATUS.md`/`docs/HANDOFF.md` para o próximo passo exato.
