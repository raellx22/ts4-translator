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

## DEC-010 — App rodado e clicado de verdade pela primeira vez; dois achados reais: `AsyncRelayCommand` engolia exceções, e `FileOpenPicker` só funciona quando o app roda de um pacote MSIX instalado, não via `dotnet run`

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

**Segundo achado, causa raiz confirmada (não mais hipótese):** depois da correção acima, clicar
"Importar packages" ainda produzia, de forma 100% reproduzível — inclusive quando o próprio usuário
clicou manualmente, sem nenhuma automação envolvida —:

```
System.Runtime.InteropServices.COMException (0x80004005): E_FAIL
```

lançada de dentro de `FileOpenPicker` (tanto `PickMultipleFilesAsync` quanto `PickSingleFileAsync` —
não é específico de seleção múltipla). A primeira hipótese testada foi elevação de privilégios (a
sessão de ferramentas usada para desenvolver roda como Administrador), mas o **usuário reproduziu o
mesmo erro clicando manualmente, sem elevação nenhuma** — isso descarta elevação como causa.

A causa real: **o app estava rodando via `dotnet run`, que registra o pacote de forma "solta"
(dev-loop registration, apontando direto para a pasta `bin\...\AppX`) em vez de instalá-lo de verdade.**
Nessa forma de registro, o broker do Windows que hospeda `FileOpenPicker`/`FileSavePicker` falha
consistentemente com `E_FAIL` ao tentar validar a identidade do pacote chamador. Confirmado gerando um
MSIX real (`dotnet build -p:GenerateAppxPackageOnBuild=true -p:PublishTrimmed=false -p:PublishReadyToRun=false`,
assinado com um certificado de desenvolvimento auto-gerado cujo Subject bate com o `Publisher` do
`Package.appxmanifest`, e instalado com `Add-AppxPackage`) — rodando esse pacote instalado, sem
nenhuma mudança de código, `FileOpenPicker` abriu normalmente e o fluxo completo de importação
funcionou (arquivo `.package` sintético selecionado → STBL contada → `TranslationProject` criado →
aparece na lista de recentes), confirmado com screenshot real.

**Decisão:** Manter o código como está — `FileOpenPicker` via `WinRT.Interop.InitializeWithWindow` é
a abordagem correta (seção 2 do brief pede integração nativa com o Explorer, não um diálogo Win32
clássico). O ajuste é no **processo de verificação/desenvolvimento**, não no código do app: qualquer
teste manual do fluxo de importação (ou de qualquer outra API mediada por broker do Windows — picker,
notificação, compartilhamento) precisa rodar a partir de um pacote MSIX instalado
(`dotnet build ... -p:GenerateAppxPackageOnBuild=true ...` + assinar + `Add-AppxPackage`), não via
`dotnet run`. `dotnet run` continua perfeitamente válido para o dia a dia de desenvolvimento (UI em
geral, navegação, tudo que não depende de um broker) — só não serve para validar picker.

**Alternativas descartadas:** declarar `allowElevation` no manifesto (a causa nunca foi elevação);
trocar `FileOpenPicker` por diálogo Win32 clássico (perderia a integração nativa que o brief pede, e
não era necessário — o problema era só o modo de lançamento).

**Consequências:** Documentar esse procedimento de empacotamento/instalação em `docs/BUILD.md` (a
criar) para que futuras sessões não percam tempo reinvestigando a mesma causa. `HomePage` está
confirmada funcionalmente completa para o caso feliz (import → contagem de STBL → projeto criado) —
ver `docs/STATUS.md`.

---

## DEC-011 — Pipeline completo (extrair→traduzir→exportar) confirmado de ponta a ponta

**Contexto:** Depois do primeiro fluxo de importação funcionar (DEC-010), o pedido seguinte foi
"finalizar" o suficiente para um teste prático real: importar um mod de verdade, traduzir, e exportar
um package traduzido de verdade. Isso exigiu completar várias peças que só existiam como contrato/teste
isolado até aqui.

**O que foi adicionado nesta rodada, todo coberto por testes automatizados (101 no total) e depois
verificado rodando o app de verdade (empacotado como MSIX, ver DEC-010/`docs/BUILD.md`):**

- `IProjectStringService`/`EfProjectStringService` — persiste as strings individuais de um projeto
  (não só a contagem de STBLs), com upsert por hash que preserva traduções existentes ao reimportar
  a mesma versão de um mod (seção 25) e remove hashes que desapareceram da fonte.
- `ISecretStore`/`DpapiSecretStore` — API key de provider criptografada via DPAPI do Windows, nunca
  em texto puro (seção 19), em arquivo próprio fora do SQLite.
- `IProviderConfigurationService`/`EfProviderConfigurationService` — guarda endpoint/model/temperatura
  no SQLite e delega a API key ao `ISecretStore`.
- `ITranslationProviderFactory`/`OpenAICompatibleProviderFactory` — constrói um `OpenAICompatibleProvider`
  de verdade a partir da configuração salva, sem a UI precisar saber qual provider concreto existe por
  trás (seção 4).
- `IPackageService.WriteStringTablesAsync` — nova sobrecarga que escreve **múltiplas** STBLs num único
  package de saída (um projeto pode ter mais de uma STBL de origem; a exportação final deve ser um
  arquivo só, não um por STBL). `WriteStringTableAsync` (a original, uma STBL) passou a delegar para
  essa.
- `EditorPage`/`EditorPageViewModel` — lista as strings de um projeto, botão "Traduzir pendentes"
  (roda o `TranslationOrchestrator` já existente contra o provider configurado, atualiza status/issue
  por string) e botão "Exportar" (agrupa por STBL de origem, troca o locale via `IStringTableService.WithLocale`,
  escreve via `WriteStringTablesAsync`, e só reporta sucesso depois de **reabrir o arquivo gerado e
  comparar cada string byte a byte com o que deveria ter sido escrito** — seção 28, round-trip
  validation real, não assumida).
- `SettingsPage`/`SettingsPageViewModel` — formulário para configurar o provider (nome, endpoint,
  modelo, API key opcional) e um botão "Testar conexão" que chama o provider de verdade com uma
  string trivial.
- Navegação Home → Editor: clique num item da lista de projetos recentes usa `Frame.Navigate(typeof(EditorPage), project.Id)`
  — a primeira navegação com parâmetro do app; `EditorPage.OnNavigatedTo` lê `e.Parameter as Guid` e
  aciona `ViewModel.LoadCommand`.

**Verificado de ponta a ponta nesta sessão, com um `.package` sintético (gerado por um mini-projeto
console reaproveitando `Sims4PackageService`, nunca commitado)**: importar → 4 strings extraídas e
persistidas → abrir o projeto no Editor → todas as 4 strings visíveis com status "Pendente" → clicar
"Traduzir pendentes" sem provider configurado mostra a mensagem correta pedindo para configurar em
Configurações (não trava, não quebra) → clicar "Exportar" sem nenhuma tradução feita usa o texto
original como fallback, gera `exemplo2_pt-BR.package`, e reporta **"Exportado e validado com sucesso"**
depois da validação de round-trip passar de verdade.

**O que ainda não foi verificado com um provider real** (nenhum LM Studio nem chave NVIDIA disponível
nesta sessão): o botão "Traduzir pendentes" de fato preenchendo traduções via HTTP real. O código é o
mesmo `OpenAICompatibleProvider` já testado com HTTP falso (74→91 testes cobrindo essa peça
especificamente) mas o caminho HTTP real ponta a ponta dentro do app nunca rodou. Ver `docs/HANDOFF.md`.

**Decisão:** Edição manual de tradução na `EditorPage` foi deixada **somente leitura** por enquanto
(`TextBlock`, não `TextBox`) — implementar um campo editável que parecesse funcionar mas não
persistisse a edição no banco seria pior do que não ter o campo (brief seção 60: não deixar
funcionalidade pela metade). Persistir edição manual é um item real do roadmap (seção 30/31), não
feito aqui.

**Consequências:** A próxima sessão (ou o usuário, com um provider real configurado) deveria validar
o caminho de tradução com HTTP de verdade antes de qualquer outra feature nova — é o único elo do
pipeline principal ainda não exercitado de ponta a ponta com dados reais.

---

## DEC-012 — Edição manual persistente + histórico, drag-and-drop, diff de reimportação, e Glossário

**Contexto:** O usuário pediu para "concluir o projeto" antes de fazer seu próprio teste prático
completo (avaliação e correção de bugs ficam para uma rodada seguinte). Esta rodada fechou quatro
itens do roadmap que dependiam uns dos outros ou do mesmo ponto de extensão (`EfProjectStringService`,
`ITranslationProvider`/`ITranslationOrchestrator`):

**1. Edição manual persistente + histórico (seção 30/31, resolve a limitação deixada em aberto na
DEC-011):**
- `EditorPage` agora tem a coluna de tradução como `TextBox` editável (`AcceptsReturn="True"`), salva
  no `LostFocus` via `EditorPageViewModel.SaveManualEditAsync` → `UpdateTranslationAsync(..., ProjectStringStatus.ManuallyEdited, ...)`.
- Toda sobrescrita de uma tradução não-nula grava a versão anterior em `ProjectStringRevisionRecord`
  (`ChangedAtUtc`, texto anterior) **antes** de sobrescrever — nunca a primeira tradução (não há nada
  antes dela para preservar). `GetHistoryAsync` devolve mais recente primeiro, ordenado client-side
  (mesma lição do DEC-008 — `DateTimeOffset` não ordena em SQL no SQLite).
- Adicionado filtro por status (Todas/Pendentes/Traduzidas/Revisar/Editadas manualmente) e busca de
  texto (Original + Tradução, case-insensitive) na `EditorPage`, sobre uma lista mestra `_allRows` em
  memória — o projeto de um mod real tem no máximo alguns milhares de strings, filtrar em memória é
  suficiente e evita reconsultar o banco a cada tecla digitada.

**2. Drag-and-drop de `.package` na Home + diff de reimportação (seção 25, "mod update engine" —
groundwork; fuzzy matching completo fica para depois):**
- `HomePageViewModel.ImportPackagesAsync`/o novo `Drop` handler foram unificados em `RunImportAsync`,
  que aceita qualquer fonte de lista de arquivos (picker ou `DataPackageOperation.Copy` do drop) —
  evita duplicar a lógica de criar projeto + extrair STBLs + reportar status entre os dois caminhos.
- `IProjectStringService.ReplaceStringsAsync` agora retorna `ImportDiffResult` (New/Changed/Unchanged/Removed/TotalAfterImport)
  em vez de `void`. Quando o hash de uma entrada já existe mas o texto original mudou e **já havia
  uma tradução**, a entrada é marcada `NeedsReview` com um resumo explicando o motivo, mas a tradução
  antiga **não é descartada** — o tradutor decide se ela ainda serve ou precisa de revisão, em vez do
  sistema decidir por ele apagando o trabalho já feito.
- A Home mostra a contagem por categoria depois de cada import/reimport (ex.: "3 nova(s), 1 alterada(s),
  12 reaproveitada(s), 1 removida(s)") — dá visibilidade real do que uma nova versão do mod mudou, sem
  ainda ser o fuzzy-matching completo de string similar-mas-não-idêntica que a seção 25 também pede.

**3. Glossário integrado ao pipeline de tradução (seção 23):**
- `GlossaryTerm`/`IGlossaryService`/`EfGlossaryService`: termo tem `NeverTranslate` (nunca traduzir,
  ex. nomes de marca) e `CaseSensitive`; pode ser global (`ProjectId == null`, visível a todo projeto)
  ou específico de um projeto. `GetTermsAsync(projectId)` traz `WHERE ProjectId == null OR ProjectId == projectId`.
- **Mudança de assinatura que se propagou por três camadas:** `ITranslationProvider.TranslateBatchAsync`
  e `ITranslationOrchestrator.TranslateAsync` ganharam um parâmetro opcional
  `IReadOnlyList<GlossaryTerm>? glossaryTerms = null` (opcional e por último, antes do `CancellationToken`,
  para não quebrar nenhum chamador existente). `TranslationOrchestrator` filtra, **por batch**, apenas
  os termos cujo `GlossaryTerm.AppearsIn(text)` bate com algum item daquele batch específico — nunca
  envia o glossário inteiro a cada chamada de provider (economiza tokens de prompt e reduz ruído para
  o modelo). `OpenAICompatibleProvider.BuildSystemPrompt` acrescenta uma seção "Glossary - apply these
  exactly" ao prompt de sistema quando há termos relevantes; ganhou `SupportsGlossary: true` e
  `SupportsCustomPrompt: true` em `Capabilities`.
- Quando nenhum termo do lote de entrada bate com o batch atual, o filtro (`Where(...).ToList()`)
  produz uma lista **vazia**, não `null` — só é `null` quando a lista de entrada em si é `null`/vazia.
  Para qualquer provider que cheque `glossaryTerms is { Count: > 0 }` (como o `OpenAICompatibleProvider`
  faz), lista vazia e `null` são equivalentes; documentado aqui para não confundir uma futura sessão
  escrevendo um novo provider.

**Testes adicionados:** 6 em `EfProjectStringServiceTests` (diff de import + histórico), 4 em
`TranslationOrchestratorTests` (filtragem de glossário por batch, incluindo o caso de lista vazia
acima), 4 em `OpenAICompatibleProviderTests` (prompt com/sem glossário, `NeverTranslate`), 6 em um novo
`EfGlossaryServiceTests` (CRUD + escopo global vs. projeto). Total da suíte: 101 → 121 testes, todos
passando (`Core.Tests` continua sem nenhum teste — apenas scaffolding do template, não uma regressão).

**Alternativas:** Para o glossário, uma interface separada `ITranslationProvider.TranslateBatchAsync`
sem o parâmetro, com o glossário injetado só no `TranslationOrchestrator` via um wrapper de prompt
genérico — rejeitada porque cada provider precisa decidir *como* comunicar o glossário ao seu próprio
formato de prompt (um provider de tradução por API dedicada como DeepL não usa prompt de sistema, e
precisaria de um outro mecanismo).

**Consequências:** Qualquer novo `ITranslationProvider` precisa decidir explicitamente se usa
`glossaryTerms` (`SupportsGlossary: true`) ou o ignora (`false`) — não há comportamento padrão
implícito. UI ainda não tem uma tela de gerenciamento de glossário (CRUD só existe via `IGlossaryService`/testes
de integração) — é o próximo item natural de UI a fazer.

---

## DEC-013 — Tela de Glossário: só escopo global por enquanto; bug real de binding encontrado e corrigido ao clicar de verdade

**Contexto:** Seguindo o item "próximo" deixado pela DEC-012, foi adicionada `GlossaryPage`/`GlossaryPageViewModel`
(nova entrada de navegação "Glossário" em `MainWindow.xaml`), a primeira UI para `IGlossaryService`.

**Decisão de escopo:** A página gerencia apenas termos **globais** (`AddTermAsync(..., projectId: null)`,
`GetTermsAsync(projectId: null)`) — a forma mais comum de uso (nomes próprios, marcas que valem para
qualquer mod). Termos por projeto continuam possíveis via `IGlossaryService` (o parâmetro existe), mas
sem UI dedicada ainda; adicionar isso exigiria a página saber em qual projeto o usuário está, o que
significa passá-la como parâmetro de navegação a partir do Editor — não fazer isso agora seria
sub-escopar a tarefa, mas fazer teria dobrado o tamanho da mudança para um caso de uso secundário.

**Bug real encontrado ao rodar o app de verdade (não hipotético):** a primeira versão do botão "Remover"
de cada linha usava `Command="{Binding ElementName=Root, Path=ViewModel.DeleteTermCommand}"` (Binding
clássico, não `x:Bind`, para alcançar o ViewModel da página a partir de dentro do `DataTemplate` do
`ListView`, cujo `x:DataType` é `GlossaryTerm`, não a página). Ao testar de verdade — rodar o app via
`dotnet run`, navegar até Glossário via UI Automation (`SelectionItemPattern`), adicionar um termo,
clicar "Remover" via `InvokePattern` — o clique não fazia nada: o `ListItem` do termo continuava lá,
sem nenhuma mensagem de erro. `ElementName` dentro de um `DataTemplate` de `ListView` em WinUI 3 não
resolveu o elemento `Root` de fora do template de forma confiável (mesmo com `x:Name="Root"` na `Page`).

**Corrigido:** trocado para um `Click` no code-behind (`GlossaryPage.xaml.cs`), lendo o item pelo
`Tag="{x:Bind}"` do próprio botão (`sender is FrameworkElement { Tag: GlossaryTerm term }`) e chamando
`ViewModel.DeleteTermCommand.ExecuteAsync(term)` — o mesmo padrão que `EditorPage.xaml.cs` já usa para o
`LostFocus` da tradução. Reverificado depois da correção: adicionar termo → aparece na lista → navegar
Home→Glossário (força `LoadAsync` de novo, prova que persistiu no SQLite, não só em memória) → termo
ainda lá → clicar "Remover" → mensagem "Termo removido" aparece e o item some → navegar Home→Glossário
de novo → item continua ausente (prova que o `DELETE` chegou no banco, não só na lista em memória).

**Motivo para registrar isto:** é a segunda vez nesta sessão (a primeira foi o `AsyncRelayCommand`
engolindo exceção, DEC-010) que um bug só apareceu ao clicar de verdade, não ao compilar. Reforça que
`Binding`/`ElementName` clássico atravessando a fronteira de um `DataTemplate` de lista, em WinUI 3, não
é confiável — preferir sempre `Tag="{x:Bind}"` + evento de code-behind para esse padrão específico
(item de uma lista precisando invocar um comando do ViewModel da página), como já era feito em
`EditorPage`.

**Consequências:** Qualquer botão futuro dentro de um `ItemTemplate` de `ListView`/`GridView` que precise
chamar um comando do ViewModel da página (não do item) deve usar `Tag="{x:Bind}"` + `Click` no
code-behind, não `Binding ElementName=...Command`. Verificado rodando o app de verdade (não só
compilado) — ver método em `docs/HANDOFF.md` sobre como fazer isso com UI Automation via PowerShell.

---

## DEC-014 — Preset "NVIDIA NIM" em Configurações: só preenche endpoint/model, não afirma qualidade validada

**Contexto:** A Fase 5 do roadmap identificou que `OpenAICompatibleProvider` já deveria funcionar contra
o endpoint da NVIDIA (compatível com OpenAI), mas o formato de prompt exato esperado por
`nvidia/riva-translate-4b-instruct-v2` nunca foi validado com uma API key real nesta sessão (duas
fontes de pesquisa discordam — ver Fase 0/5 do ROADMAP). Digitar manualmente
`https://integrate.api.nvidia.com/v1` e o nome exato do modelo é tedioso e propenso a erro de digitação.

**Decisão:** Adicionar um botão "Usar preset NVIDIA NIM" em `SettingsPageViewModel.ApplyNvidiaNimPresetCommand`
que só preenche `Name`/`Endpoint`/`Model` nos campos do formulário — não salva automaticamente (o usuário
ainda revisa e clica "Salvar"), não preenche a API key (é um segredo do usuário), e não afirma em
nenhum texto que a tradução foi validada como funcional. Isso evita repetir digitação sem fingir que o
formato de prompt do Riva Translate já foi confirmado — se o usuário testar e o resultado não fizer
sentido, é exatamente o tipo de achado que a rodada de correção pós-teste deveria capturar.

**Verificado rodando o app de verdade** via UI Automation: clicar no botão preenche os três campos com
os valores esperados, confirmado lendo `ValuePattern.Current.Value` de cada `TextBox` depois do clique.

**Alternativas:** Um `ComboBox` de "provider conhecido" com múltiplos presets (LM Studio, NVIDIA NIM,
outros) — mais estrutura do que o necessário enquanto só há um preset real para oferecer; revisitar
quando um segundo preset (ex. um servidor OpenAI-compatible comum) fizer sentido.

**Consequências:** Quando o formato de prompt do Riva Translate for validado com uma API key real
(ver Fase 5 do ROADMAP), atualizar o texto do preset/documentação para refletir isso — hoje o preset é
só conveniência de digitação, não uma integração validada ponta a ponta.

---

## DEC-015 — Translation Profiles (seção 22): instruções livres por projeto, não uma biblioteca de presets nomeados; e um gap real do glossário corrigido no processo

**Contexto:** A seção 22 do brief pede "Translation Profiles: presets de estilo + instruções
customizadas plugadas no prompt". Implementar uma biblioteca completa de presets nomeados reutilizáveis
entre projetos (CRUD de perfis, seleção de qual perfil um projeto usa, etc.) seria essencialmente
duplicar o esforço já feito para o Glossário (nova entidade, serviço, migration, tela) para um conceito
que ainda não tem um segundo caso de uso concreto puxando por essa flexibilidade.

**Decisão de escopo:** Implementar a parte que entrega valor real imediatamente — um campo de texto
livre `TranslationProject.CustomInstructions` (ex.: "mantenha um tom informal e brincalhão"),
persistido por projeto, editável na `EditorPage`, e propagado ao pipeline de tradução exatamente como
o glossário: `ITranslationProvider.TranslateBatchAsync`/`ITranslationOrchestrator.TranslateAsync`
ganham um parâmetro opcional `customInstructions` (depois de `glossaryTerms`, antes do
`CancellationToken`) repassado a cada batch sem filtragem — diferente do glossário, instruções de
estilo não dependem do conteúdo de um batch específico, então não há nada para filtrar por lote.
`OpenAICompatibleProvider.BuildSystemPrompt` acrescenta uma seção "Additional style/tone instructions"
quando presente. Isto **não** é uma biblioteca de perfis nomeados reutilizável entre projetos — é
instruções de um projeto, para aquele projeto. Documentar isso honestamente em vez de inflar o
`TranslationMemoryEntry.ProfileName` (que continua `null`) para fingir suporte a perfis nomeados que
não existem.

- Migration `AddProjectCustomInstructions` adiciona a coluna `CustomInstructions` (nullable) a
  `ProjectRecord`. `IProjectService.UpdateCustomInstructionsAsync` trata string vazia/só espaço como
  "limpar" (grava `null`), igual ao padrão já usado em `SettingsPageViewModel` para a API key.

**Gap real encontrado e corrigido no processo:** ao editar `EditorPageViewModel.TranslateAsync` para
passar `customInstructions`, ficou evidente que a chamada a `translationOrchestrator.TranslateAsync`
**nunca passava `glossaryTerms`** — a integração de glossário da DEC-012 foi construída e testada em
todas as camadas (`Core`/`Persistence`/`Providers`/`Translation`), mas o único ponto de chamada real da
UI (o botão "Traduzir pendentes") nunca buscava os termos via `IGlossaryService` para repassar ao
orquestrador. Ou seja: o glossário funcionava perfeitamente em teste, mas **nunca era de fato aplicado
ao clicar "Traduzir pendentes" no app real** até agora. Corrigido injetando `IGlossaryService` em
`EditorPageViewModel` e chamando `glossaryService.GetTermsAsync(_project.Id)` antes de
`TranslateAsync`. Este é o tipo de lacuna que só aparece ao seguir o caminho de chamada ponta a ponta,
não ao olhar cada camada isoladamente — vale a pena, ao adicionar um novo parâmetro de pipeline no
futuro, grep por todos os call sites de `TranslateAsync`/`TranslateBatchAsync`, não só os que o
compilador força a atualizar (um parâmetro opcional não força nada).

**Testes adicionados:** 3 em `EfProjectServiceTests` (round-trip, limpar com string vazia, id
desconhecido não lança), 2 em `TranslationOrchestratorTests` (repassado a cada batch, `null` quando
omitido), 2 em `OpenAICompatibleProviderTests` (aparece no prompt quando presente, ausente quando não).
Total: 121 → 129 testes.

**Consequências:** Se um segundo caso de uso real por uma biblioteca de perfis nomeados aparecer
(compartilhar o mesmo estilo entre vários projetos, por exemplo), promover `CustomInstructions` para
uma entidade `TranslationProfile` própria nesse momento — não antecipadamente. Qualquer parâmetro
opcional futuro adicionado a `ITranslationOrchestrator.TranslateAsync` deve ter todo call site real
(não só os testes) auditado manualmente, pelo motivo acima.

---

## DEC-016 — Fuzzy matching em Translation Memory: Levenshtein em memória, limiar conservador de 97% para reuso automático

**Contexto:** A seção 26 do brief (e a seção 63, que chama a combinação de translation memory + fuzzy
matching de "killer feature" do produto) pede que uma correção pequena no texto original de um mod
(ex.: um erro de digitação corrigido em uma atualização) ainda aproveite a tradução anterior, em vez de
tratar a string como inteiramente nova (que é o que `FindExactAsync`, por hash, faz hoje — um único
caractere diferente já produz um hash diferente e perde o reaproveitamento).

**Decisão:**
- `StringSimilarity.Ratio` (`Core/Memory/`) implementa distância de Levenshtein clássica (matriz de
  duas linhas, O(n·m) tempo/O(min(n,m)) memória) e converte para uma razão 0.0–1.0
  (`1 - distância / comprimento_máximo`). Testado isoladamente em `TS4Translator.Core.Tests` (o
  projeto estava vazio até agora — só scaffolding do template — esta é a primeira suíte real nele).
- `ITranslationMemory.FindSimilarAsync(text, source, target, minSimilarity = 0.85)` retorna o
  `FuzzyMatchResult` (entrada + similaridade) mais próximo acima do limiar, ou `null`. Implementado em
  `SqliteTranslationMemory` trazendo todas as entradas do par de locales para memória e pontuando uma a
  uma — não há função de similaridade de string no SQL do SQLite. Aceitável na escala real de uma
  translation memory de um modder (dezenas de milhares de entradas, não milhões); revisitar com um
  índice mais esperto (n-gram/trigram) só se isso aparecer como gargalo real medido, não
  preventivamente.
- `TranslationOrchestrator` chama `FindSimilarAsync` **depois** de `FindExactAsync` falhar e **antes**
  de enviar o item ao provider, com um limiar próprio e deliberadamente alto —
  `FuzzyReuseThreshold = 0.97` — porque esse ponto do pipeline **substitui silenciosamente uma chamada
  ao provider**, sem nenhuma UI de "escolha entre candidatos" para o tradutor decidir; só dispara para
  texto quase idêntico (uma pontuação ou espaço mudou), nunca para reformulações genuinamente
  diferentes. O resultado reaproveitado sempre volta com `Success: false` e um
  `ValidationIssueCode.FuzzyMatchReused` (severidade `Warning`), então cai como `NeedsReview` no
  Editor — nunca é apresentado como uma tradução "confirmada" igual a um hit exato ou a uma tradução
  fresca do provider.

**Alternativas:** (a) Um limiar único (ex. 0.85) compartilhado entre a API genérica e o uso automático
no orquestrador — rejeitada porque 0.85 é apropriado para uma futura UI de "sugestões" onde um humano
escolhe entre candidatos, mas perigoso demais para substituir uma tradução real sem revisão nenhuma.
(b) Um algoritmo de similaridade mais sofisticado (Jaro-Winkler, embeddings) — Levenshtein já resolve o
caso concreto do brief (pequenas edições de texto) com zero dependência nova.

**Consequências:** Uma correção de texto genuinamente pequena (< 3% de diferença) é reaproveitada sem
chamar o provider, mas sempre marcada para revisão — nunca conta como "traduzida" silenciosamente. Uma
UI de "sugestões de fuzzy match" navegável (mostrar os 3-5 candidatos mais próximos para o tradutor
escolher, usando o limiar mais permissivo de 0.85 já exposto pela API) continua não implementada — é o
próximo passo natural se isso se mostrar valioso na prática.

---

## DEC-017 — Bug real reportado pelo usuário: `ArgumentNullException: translatedText` travava a tradução inteira quando o provider respondia `"translation": null` para um item

**Contexto:** Primeiro teste prático do usuário contra um provider HTTP real (rodando o app instalado
como MSIX) produziu `Falha ao traduzir (ArgumentNullException): Value cannot be null. (Parameter
'translatedText')` — a primeira falha real encontrada testando fora desta sessão. Rastreamento: o
provider respondeu com um item estruturalmente válido mas `"translation": null` (ou o campo ausente).
`OpenAICompatibleProvider.PromptResponseItem` estava declarado com `string Translation` (não-nulo), mas
`System.Text.Json` **não** valida nulabilidade de tipo referência não-nulo na desserialização — a
anotação é só para o compilador, não para o runtime. O `null` passava intacto até
`new TranslationResultItem(r.Id, r.Translation)`, cujo tipo (`Core/Translation/TranslationResultItem.cs`)
também é `string` não-nulo só por convenção, sem checagem de fato. `TranslationOrchestrator` então
passava `candidates[0].TranslatedProtectedText` direto para `TokenProtector.Restore`, que faz
`ArgumentNullException.ThrowIfNull(translatedText)` por design (rejeitar null é o comportamento correto
*daquele* método) — só que nada antes dele impedia um `null` de chegar ali, e a exceção não era
capturada em lugar nenhum dentro do loop de batch, derrubando a tradução do batch inteiro (não só do
item problemático) até o `catch` genérico em `EditorPageViewModel.TranslateAsync`.

**Decisão — corrigido em duas camadas, cada uma no nível certo:**
1. **Na origem** (`OpenAICompatibleProvider`): `PromptResponseItem.Id`/`.Translation` passaram a ser
   `string?` (documentando a realidade, não escondendo-a atrás de uma anotação otimista), e
   `TranslateBatchAsync` filtra (`.Where(r => r.Id is not null && r.Translation is not null)`) antes de
   construir `TranslationResultItem` — mesma filosofia já aplicada a JSON truncado/malformado
   (`SalvageCompleteObjects`, seção 46): um item que não é utilizável é descartado, nunca repassado como
   um `null` disfarçado de `string`.
2. **Defesa em profundidade no orquestrador** (`TranslationOrchestrator.TranslateBatchAsync`): um novo
   guard checa `candidates[0].TranslatedProtectedText is null` **antes** de chamar
   `tokenProtector.Restore`, tratando como o mesmo caso de `ProviderResultMissing` já existente (o
   provider não devolveu nada utilizável para aquele id) — item vira uma falha isolada, resto do batch
   continua normalmente. Esta camada existe porque `ITranslationProvider` é uma interface com
   implementações futuras (Argos, DeepL, ...) que a orquestração não pode auditar uma a uma; nenhuma
   implementação individual, por mais corrigida que esteja, deveria ser a única linha de defesa contra
   um contrato violado.

**Por que os testes existentes não pegaram isso antes:** toda a suíte de `OpenAICompatibleProviderTests`
usava JSON de teste que sempre tinha `"translation"` como uma string de verdade — nenhum teste
verificava `"translation": null` explicitamente, só ausência de objeto inteiro (JSON truncado) ou
resposta completamente não-parseável. É uma lacuna de cobertura diferente de "JSON malformado": aqui o
JSON é perfeitamente válido, só que semanticamente incompleto.

**Testes adicionados:** 2 em `OpenAICompatibleProviderTests` (`"translation": null` descartado,
`"id": null` descartado), 1 em `TranslationOrchestratorTests` (`FakeProviderMisbehavior.NullTranslationForFirstResult`,
simulando um provider mal-comportado que constrói um `TranslationResultItem` com `null` mesmo com o tipo
declarado não-nulo — prova que a defesa do orquestrador funciona independente de qual provider é usado).
Total: 144 → 147 testes.

**Consequências:** Qualquer novo `ITranslationProvider` que desserializa a resposta de uma API externa
deve tratar campos "obrigatórios" como potencialmente nulos no tipo de desserialização (`string?`, não
`string`), e filtrar antes de construir `TranslationResultItem` — declarar um campo como não-nulo não o
torna não-nulo de fato quando a fonte é uma resposta de rede fora do controle do app.

---

## DEC-018 — Feedback real de progresso ao traduzir: chunking com checkpoint no ViewModel, não no orquestrador; cancelamento seguro

**Contexto:** Testando a tradução de um mod com ~25 mil strings via NVIDIA NIM, o usuário reportou que
não havia nenhum jeito de saber se o app estava de fato traduzindo ou travado — só um `ProgressRing`
indeterminado e um texto de status que não mudava até o fim. Investigação revelou um problema mais
sério por trás da queixa de UX: `EditorPageViewModel.TranslateAsync` chamava
`translationOrchestrator.TranslateAsync` **uma única vez com todas as strings pendentes**, e só
persistia qualquer resultado no banco (`UpdateTranslationAsync`) depois que a chamada inteira
retornasse. Ou seja, para um lote de 25 mil strings — que a ~100 itens por chamada HTTP (limite padrão
do `BatchBuilder`) significa ~250 chamadas sequenciais ao provider — **nada era salvo até o fim**: uma
falha, um fechamento do app, ou uma queda de conexão a qualquer momento no meio do caminho perderia
100% do trabalho já traduzido, não só o que faltava.

**Decisão:**
- `EditorPageViewModel.TranslateAsync` divide `pending` em blocos de `TranslationChunkSize = 100`
  (mesmo valor do limite padrão de item do `BatchBuilder`, então um bloco tipicamente corresponde a uma
  única chamada HTTP) via `IEnumerable<T>.Chunk()`, chamando `ITranslationOrchestrator.TranslateAsync`
  **uma vez por bloco**, e persistindo (`UpdateTranslationAsync`) o resultado de cada bloco antes de
  seguir para o próximo. Isso é implementado inteiramente no ViewModel, não em
  `ITranslationOrchestrator` — a interface do orquestrador não muda, porque persistência é
  responsabilidade da camada de app/projeto, não do pipeline de tradução em si (mesmo raciocínio já
  aplicado ao glossário/instruções customizadas nas DEC-012/DEC-015).
- Duas novas propriedades observáveis, `ProgressCurrent`/`ProgressTotal`, atualizadas a cada bloco
  concluído, alimentam um `ProgressBar` real (não mais só o `ProgressRing` indeterminado) na
  `EditorPage`, junto com o texto de status mostrando contagem corrente de sucesso/revisão/falha em
  tempo real — não só ao final.
- Como o checkpoint por bloco já existe, cancelamento se torna seguro "de graça": um botão "Cancelar
  tradução" (visível só durante `IsTranslating`) cancela um `CancellationTokenSource` próprio do
  método; o loop verifica `cancellationToken.ThrowIfCancellationRequested()` no início de cada bloco, e
  o token é repassado à chamada do orquestrador e a `UpdateTranslationAsync` também. Cancelar no meio
  de um lote de 25 mil perde, no pior caso, só o bloco de até 100 strings em andamento — nunca o
  trabalho já persistido.

**Verificado rodando o app de verdade** (não só compilado): montado um servidor HTTP local fake
imitando o contrato `chat/completions` (com latência artificial de 800ms-2.5s por chamada, para dar
tempo de observar), importado um `.package` sintético com 500 strings, e confirmado via UI Automation
que — durante a tradução — a `ProgressBar` mostra `Value`/`Maximum` reais (ex.: `0/500` enquanto a
primeira chamada HTTP está em voo) e o botão "Cancelar tradução" aparece/desaparece corretamente.
**Achado colateral, não um bug**: como as strings sintéticas de teste eram quase idênticas entre si
(mesma frase-molde, variando só um número), o fuzzy matching (DEC-016) reaproveitou a maior parte dos
blocos 2 em diante contra o que já tinha sido salvo do bloco 1 — confirma que a interação entre
progresso incremental e fuzzy matching funciona corretamente em conjunto (cada bloco só chama o
provider para o que realmente precisa), mas também é um lembrete de que mods com strings numeradas
quase idênticas (ex. "Nível 1", "Nível 2", ...) vão gerar bastante reaproveitamento fuzzy — sempre
marcado para revisão, nunca aplicado silenciosamente, então é seguro mas vale conferir esses casos.

**Alternativas:** Expor um `IProgress<T>` como parâmetro de `ITranslationOrchestrator.TranslateAsync` —
rejeitada porque resolveria só o progresso, não o problema mais sério de persistência tardia; o
chunking no ViewModel resolve os dois de uma vez sem tocar num contrato já usado por 60+ testes.

**Consequências:** Qualquer fluxo futuro que também dispare tradução em massa (ex.: uma ação em lote na
Home) deve seguir o mesmo padrão de chunking + persistência incremental, não voltar a chamar o
orquestrador uma única vez para milhares de itens.

---

## DEC-019 — Providers DeepL e Google Gemini; `ProviderConfiguration` ganha `Kind` para suportar múltiplos tipos de provider

**Contexto:** O usuário notou que a tela de Configurações só tinha um formulário (o provider
OpenAI-compatible, usado para NVIDIA NIM/LM Studio) e perguntou pelos outros providers listados no
brief original (seção 18: DeepL, Google, OpenAI dedicado, Claude). Investigação confirmou: só
`OpenAICompatibleProvider` existia no código — os demais nunca tinham sido implementados (documentado
honestamente no ROADMAP como "não iniciados", mas não visível olhando só a UI). Pedido do usuário:
implementar DeepL e Google Gemini.

**Decisão — mudança de arquitetura, não só duas classes novas:** o app só suportava configurar **um**
tipo de provider (`ProviderConfiguration` não tinha noção de "tipo"). Para suportar múltiplos tipos:

- `ProviderKind` (enum: `OpenAICompatible`, `DeepL`, `GoogleGemini`) — novo em `Core/Providers/`.
  `ProviderConfiguration.Kind` e `.Model` (agora `string?`, já que DeepL não tem conceito de modelo).
  `IProviderConfigurationService.SaveAsync` ganha um parâmetro `kind`. Migration `AddProviderKind`
  adiciona a coluna (default `"OpenAICompatible"` para linhas existentes — o único tipo que já existia
  antes desta mudança) e torna `Model` nullable.
- **API key namespaced por `Kind`**, não uma chave fixa (`$"provider:{kind}"` em vez de
  `"provider:openai-compatible"` fixo): trocar de tipo e salvar sem uma key nova (deixando em branco,
  "não mexer") não deve nem vazar a key de um tipo diferente nem apagá-la — cada tipo mantém seu
  próprio slot no `ISecretStore`, mesmo só existindo uma configuração ativa por vez.
  `EfProviderConfigurationService.GetApiKey()` (síncrono por design de interface) passou a fazer uma
  leitura própria do banco para saber o `Kind` atual antes de resolver o nome do secret.
- **Bug real pego pelo próprio teste que escrevi para essa mudança**: `HasApiKey` inicialmente só era
  atualizado dentro do `if (apiKey is not null)` — trocar de `Kind` passando `apiKey: null` (semântica
  "não mexer na key deste save") deixava `HasApiKey` com o valor *do tipo anterior*, então uma
  configuração DeepL recém-criada aparecia como "tem API key" mesmo nunca tendo recebido uma. Corrigido
  recalculando `HasApiKey` a partir do `ISecretStore` a cada `SaveAsync`, nunca herdando o valor antigo
  do registro.
- `DeepLProvider` (`Providers/DeepL/`) — API dedicada de tradução, não um chat model:
  `SupportsGlossary`/`SupportsCustomPrompt` reportados como `false` honestamente (DeepL não tem
  conceito de prompt de sistema para injetar isso; o recurso de glossário nativo da própria DeepL e o
  `tag_handling=xml` para proteção de token mais robusta ficam documentados como melhoria futura, não
  implementados). DeepL correlaciona resultado por **posição no array**, não por um id que a chamada
  controla — reconciliado internamente no provider (zip por índice) para que
  `TranslationOrchestrator` continue só vendo resultados com `Id` explícito, nunca precisando confiar
  em posição.
- `GoogleGeminiProvider` (`Providers/GoogleGemini/`) — mesma estratégia de
  `OpenAICompatibleProvider` ("manda um array JSON, pede um array JSON de volta", com o mesmo scanner
  de recuperação de JSON truncado), porque Gemini também é um modelo generativo, só com envelope de
  request/response e cabeçalho de auth diferentes (`x-goog-api-key`, e um campo `systemInstruction`
  dedicado em vez de uma mensagem de sistema dentro de `messages`). A lógica de prompt/parsing foi
  **duplicada** de `OpenAICompatibleProvider` deliberadamente, não extraída para um helper
  compartilhado — é só o segundo provider que precisa dela; extrair uma abstração comum é razoável se
  um terceiro provider precisar do mesmo padrão, não antes ("regra de três").
- `ConfiguredProviderFactory` (`Providers/`) substitui `OpenAICompatibleProviderFactory` como a
  implementação registrada de `ITranslationProviderFactory` — único lugar que sabe mapear `ProviderKind`
  para a classe concreta certa.
- `SettingsPageViewModel`/`SettingsPage.xaml` ganham um `ComboBox` de "Tipo de provider", campo
  "Modelo" escondido quando o tipo é DeepL, e auto-preenchimento de endpoint/modelo padrão ao trocar de
  tipo — mas só quando o valor atual do campo Endpoint é vazio **ou já é um dos valores padrão
  conhecidos** (`KnownDefaultEndpoints`), nunca quando o usuário digitou algo próprio. Essa checagem
  por "é um padrão conhecido" (não só "está vazio") foi necessária porque um "só preenche se vazio"
  simples quebra ao explorar mais de um tipo em sequência: o primeiro preenchimento já deixa o campo
  não-vazio, impedindo o preenchimento do segundo tipo. Verificado rodando o app de verdade via UI
  Automation: alternar entre os três tipos preenche o endpoint certo a cada troca; um valor customizado
  sobrevive à troca de tipo sem ser sobrescrito; salvar e recarregar a página preserva `Kind`
  corretamente.

**Testes adicionados:** 12 em `DeepLProviderTests` (reconciliação por posição, mapeamento de idioma
EN-US/EN-GB/PT-BR/PT-PT, header de auth, capabilities honestas), 13 em `GoogleGeminiProviderTests`
(mesma bateria de `OpenAICompatibleProviderTests` adaptada: JSON truncado, markdown fences, tradução
nula, glossário no `systemInstruction`), 4 em `ConfiguredProviderFactoryTests` (uma por `ProviderKind`
+ ausência de configuração), 3 novos em `ProviderConfigurationTests` (round-trip com `Model: null`,
troca de tipo não vaza key do tipo anterior, voltar a um tipo já configurado restaura a key dele).
Total: 147 → 182 testes.

**Consequências:** Um quarto provider (OpenAI dedicado, Claude, ...) segue o mesmo molde: nova entrada
em `ProviderKind`, nova classe em `Providers/`, um `case` a mais em `ConfiguredProviderFactory`, uma
entrada a mais em `SettingsPageViewModel.ProviderKindOptions`. UI de glossário/perfis por tipo de
provider continua um único formulário genérico — não há telas dedicadas por tipo, só campos
condicionais (`ShowsModelField`); se um quarto tipo precisar de um campo que os outros não têm, esse
padrão pode não escalar bem e vale reconsiderar.

---

## DEC-020 — Incidente real de perda de dados: `Remove-AppxPackage` antes de reinstalar apagava a pasta de dados do app inteira; corrigido incrementando a versão do pacote a cada redeploy

**Contexto:** Depois de reinstalar o MSIX para testar a mudança de `ProviderKind` (DEC-019), a tela de
Configurações voltou a mostrar "Nenhum provider configurado ainda" — a configuração real da NVIDIA NIM
do usuário (endpoint, modelo, e a API key) tinha desaparecido. Investigação direta no banco (consulta
SQL contra `LocalCache\Local\TS4Translator\Data\ts4translator.db` do pacote instalado) confirmou:
tabela `ProviderConfigurations` vazia, e a pasta `Secrets/` (onde `DpapiSecretStore` grava a API key
criptografada) nem existia mais.

**Causa raiz confirmada:** o procedimento de reinstalação usado repetidamente ao longo desta sessão
(`Get-AppxPackage ... | Remove-AppxPackage` seguido de `Add-AppxPackage -Path $msix`, documentado em
`docs/BUILD.md` desde a DEC-010) **apaga a pasta de dados do pacote inteira**
(`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\...`) — banco SQLite e segredos DPAPI
inclusos. Isso é comportamento padrão e documentado do AppX/MSIX: remover um pacote remove os dados
por usuário associados a ele, não é um bug do Windows nem deste app. O erro foi meu: apliquei
"remover antes de instalar" (necessário *uma única vez*, na transição do registro "solto" via
`dotnet run` para o primeiro pacote assinado — ver DEC-010, onde o `Remove-AppxPackage` era necessário
para limpar o registro de dev-loop antes de instalar o pacote de verdade) como se fosse o procedimento
padrão para *toda* atualização de pacote já assinado, quando na verdade cada reinstalação subsequente
com esse padrão apagava os dados de novo, silenciosamente, sem nenhum aviso.

**Por que ninguém percebeu antes:** entre uma reinstalação e outra, várias verificações desta sessão só
reabriram o app já instalado (`Stop-Process`/`Start-Process`, sem `Remove-AppxPackage`) — dados
persistiam normalmente nesses casos, mascarando o problema. Só ficou visível porque o usuário estava
usando o app de verdade, entre sessões, com uma configuração real que valia a pena perceber a falta.

**Decisão:**
- `Package.appxmanifest`'s `Identity/@Version` deixa de ficar fixo em `1.0.0.0` — incrementado
  (`1.0.1.0`, `1.0.2.0`, ...) a cada rebuild que será reinstalado para teste. O Windows só permite
  `Add-AppxPackage` fazer um **upgrade in-place** (preservando `LocalCache`) quando a versão nova é
  maior que a instalada — com a mesma versão, teria que remover primeiro (voltando ao problema).
- `docs/BUILD.md` reescrito: `Remove-AppxPackage` antes de reinstalar **nunca mais**, exceto na
  transição única solto→assinado. O procedimento de redeploy agora é só incrementar a versão + `Add-AppxPackage`
  direto, sem remover.
- **Verificado empiricamente antes de confiar na correção**: salvei uma configuração
  marcadora (`MARKER-PERSISTENCE-TEST`) na versão `1.0.1.0`, fiz rebuild para `1.0.2.0`, reinstalei só
  com `Add-AppxPackage` (sem remover), e confirmei via UI Automation que o marcador sobreviveu ao
  upgrade. Só depois disso o procedimento novo foi documentado como confiável.

**Dado perdido, sem como recuperar:** a configuração real de NVIDIA NIM do usuário (endpoint, modelo,
API key) precisou ser reconfigurada manualmente por ele depois deste incidente. Não havia como
recuperar a API key especificamente — DPAPI decifra usando uma chave derivada do perfil do usuário do
Windows, mas o arquivo cifrado em si tinha sido apagado junto com a pasta de dados; sem o arquivo,
não há o que decifrar.

**Consequências:** Toda sessão futura que precisar reinstalar o MSIX para testar uma mudança de código
deve seguir exatamente o procedimento revisado em `docs/BUILD.md` (incrementar versão, nunca remover
antes de instalar). Isso vale tanto para dados de teste (sintéticos, descartáveis) quanto — com muito
mais cuidado — para qualquer configuração real que o usuário tenha inserido no app instalado; a partir
de agora, presumir que **qualquer** dado no pacote instalado pode ser real e irrecuperável, não só
dados de teste desta sessão.

---

## DEC-021 — Modelo padrão do Gemini (`gemini-2.0-flash`) estava obsoleto: Google descontinuou em 1º de junho de 2026

**Contexto:** O usuário reconfigurou o provider Google Gemini com a própria API key (depois do
incidente da DEC-020) e recebeu `Falha na conexão (HttpRequestException): Response status code does
not indicate success: 404 (Not Found)` ao clicar "Testar conexão". Pesquisa (`WebSearch`) confirmou:
`gemini-2.0-flash` — o modelo que `SettingsPageViewModel`/`ConfiguredProviderFactory` usavam como
padrão (DEC-019) — foi **descontinuado pelo Google em 2026-06-01**, meses antes desta sessão
(2026-08-28). Não era um bug na construção da URL/request do `GoogleGeminiProvider` (confirmado
correto: `v1beta/models/{model}:generateContent`, header `x-goog-api-key`) — o endpoint respondia
normalmente, só não reconhecia mais aquele id de modelo específico.

**Decisão:**
- Trocado o padrão para `gemini-2.5-flash` em `SettingsPageViewModel.OnSelectedProviderKindLabelChanged`
  e no fallback de `ConfiguredProviderFactory` (usado quando `configuration.Model` é `null`).
- `GoogleGeminiProviderOptions.Model` ganhou um comentário XML explícito avisando que a Google
  descontinua/renomeia ids de modelo Gemini com alguma frequência, e que um 404 em "Testar conexão" é
  a primeira coisa a suspeitar do id de modelo configurado, não da forma da requisição.
- `SettingsPageViewModel.TestConnectionAsync` ganhou um `catch` específico para
  `HttpRequestException` com `StatusCode == 404`, com uma mensagem que já aponta para o campo Modelo em
  vez do texto genérico de exceção — o único jeito de o usuário (ou uma sessão futura) diagnosticar isso
  rápido sem precisar ler código-fonte.
- **Verificado com a API key real do usuário** (não só um teste automatizado): depois da correção,
  "Testar conexão" retornou `Conexão OK. Resposta de teste: "Olá."` — primeira confirmação real de que
  `GoogleGeminiProvider` funciona ponta a ponta contra a API do Google, não só contra HTTP falso.

**Por que isso não foi pego antes:** o valor `"gemini-2.0-flash"` foi escolhido de memória (o
conhecimento deste modelo tem corte em janeiro de 2026, antes da descontinuação), sem verificação viva
contra a documentação atual da Google no momento da DEC-019 — só testado contra HTTP falso, que não
tem como detectar um model id real e obsoleto. É exatamente o tipo de "detalhe que muda com o tempo,
fora do controle do código" que já motivou avisos parecidos em DEC-006 (não confiar cegamente em doc de
terceiro) e DEC-014 (presets não afirmam qualidade validada) — a diferença aqui é que nem doc de
terceiro foi consultada, foi só memória.

**Consequências:** Qualquer id de modelo hardcoded como "padrão sensato" neste app (Gemini, e no futuro
qualquer outro provider baseado em modelo) é candidato a ficar obsoleto sem aviso — o app não tem
detecção automática de modelos disponíveis ainda (`GET /v1beta/models`/`GET /v1/models`, item já listado
no ROADMAP Fase 6). Até isso existir, checar o id de modelo configurado deveria ser o primeiro passo
diante de qualquer 404 em "Testar conexão", antes de suspeitar do código.

---

## DEC-022 — Retry com backoff para falhas transitórias de provider (503/429/5xx/timeout) — seção 11 do brief, confirmada necessária por uma falha real

**Contexto:** Traduzindo um mod real (`TURBODRIVER_WickedWhims_Tuning`) via Google Gemini, o usuário
recebeu `Falha ao traduzir (HttpRequestException): Response status code does not indicate success: 503
(Service Unavailable)` no meio da tradução — não uma vez, mas repetidamente ao tentar de novo. Ele
conferiu o painel de limite de taxa do Google AI Studio e confirmou que tinha cota de sobra (RPM/TPM/RPD
todos bem abaixo do limite). Ou seja: não era um problema de cota do usuário — é o comportamento
conhecido e documentado do nível gratuito da API Gemini, que devolve `503` quando os servidores do
próprio Google estão sob carga, independente da cota de quem chamou. O app hoje tratava **qualquer**
exceção de `TranslateBatchAsync` como fatal, abortando a tradução inteira e exigindo que o usuário
clicasse "Traduzir pendentes" de novo manualmente — o que, com um provider instável, podia significar
vários reinícios manuais para um mod grande.

**Decisão:**
- `Providers/Resilience/HttpRetry.cs` (novo, projeto `TS4Translator.Providers`) — helper estático
  `SendWithRetryAsync` que reenvia a chamada HTTP com backoff exponencial (300ms × 2^tentativa + jitter
  de 0-250ms) quando a resposta é um status transitório (`408`, `429`, `500`, `502`, `503`, `504`) ou a
  chamada lança `HttpRequestException`/`TaskCanceledException`/`TimeoutException` — nunca retenta um
  `4xx` que não seja `408`/`429` (um request malformado ou uma chave inválida não vai ter sucesso numa
  segunda tentativa). **Nunca** retenta se o `CancellationToken` do chamador já foi cancelado — o botão
  "Cancelar tradução" (DEC-018) precisa continuar funcionando de verdade, não ser engolido como se fosse
  só mais uma falha transitória para tentar de novo.
- Cada tentativa constrói um `HttpRequestMessage` novo a partir de um delegate (`Func<CancellationToken,
  Task<HttpResponseMessage>>`), nunca reenvia a mesma instância — `HttpClient` recusa reenviar um
  `HttpRequestMessage` já enviado, então o helper exige que quem chama monte a requisição de dentro do
  delegate, não fora dele.
- Os três providers (`OpenAICompatibleProvider`, `DeepLProvider`, `GoogleGeminiProvider`) passaram a
  envolver seu "montar requisição + enviar" existente numa chamada a `HttpRetry.SendWithRetryAsync`,
  com um parâmetro `int maxRetries = 3` opcional no construtor (não altera nenhuma chamada existente).
- Distinguir "timeout de `HttpClient`" de "usuário cancelou" foi a parte mais sutil: as duas coisas
  lançam `TaskCanceledException` em .NET moderno, e só dá para diferenciar checando se o
  `CancellationToken` do próprio chamador é que disparou (`cancellationToken.IsCancellationRequested`)
  — se sim, nunca retenta; se não (é um timeout interno do `HttpClient`, não uma cancelação do usuário),
  trata como falha transitória igual a qualquer outra.

**Por que não foi extraído para `Microsoft.Extensions.Http.Resilience`/Polly:** o requisito real aqui é
estreito ("tentar de novo algumas vezes com backoff em erro transitório"), e um pacote de resiliência
completo (circuit breaker, rate limiter, etc.) traria superfície e configuração muito além do que é
necessário — a mesma filosofia de "sem abstração antes de precisar" já aplicada em outras decisões
desta sessão (DEC-019, por exemplo).

**Testes adicionados:** `HttpRetryTests` (11 testes, delay injetado e instantâneo) cobre a política de
retry isoladamente — quais status são transitórios, para de tentar depois de `maxRetries`, propaga
`HttpRequestException` persistente, nunca retenta quando o token do chamador já foi cancelado, mas
retenta um timeout que não é do chamador. Cada provider ganhou um teste de integração leve confirmando
que a chamada `HttpRetry` está de fato conectada (503 seguido de sucesso, `maxRetries: 2`,
`FakeHttpMessageHandler` estendido para devolver uma sequência de respostas em vez de uma só). Os três
testes existentes "...ServerReturnsError_ThrowsHttpRequestException" passaram a usar `maxRetries: 0`
explicitamente para continuarem rápidos e determinísticos — a política de retry em si já está coberta
por `HttpRetryTests`. Total: 182 → 202 testes.

**Consequências:** Uma falha realmente permanente (chave inválida, endpoint errado, modelo inexistente)
ainda falha rápido — só status transitórios (5xx/408/429) e falhas de rede/timeout acionam retry.
`SettingsPageViewModel.TestConnectionAsync` agora pode demorar mais para reportar falha contra um
provider instável (até `maxRetries` tentativas com backoff antes de desistir) — já mostra "Testando
conexão..." durante a espera, então isso é esperado, não um bug. O item "retry/backoff" do brief
(seção 11) estava listado no ROADMAP como não iniciado; parte dele (retry por falha transitória de
rede) está pronta agora — falta ainda checkpoint/pause/resume entre sessões do app
(`TranslationJob` persistido, ainda não implementado).
