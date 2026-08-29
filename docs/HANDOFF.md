# Handoff

Leia isto antes de qualquer outra coisa se você é uma nova sessão/agente retomando este projeto.
Depois leia `README.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`,
`docs/BUILD.md`, nessa ordem (e `docs/ARGOS.md` se for mexer com o provider offline). Não reconstrua a arquitetura do zero — ela existe, compila, tem 202
testes automatizados passando, e **o caminho prático completo (importar → extrair → editor →
exportar com validação) foi verificado de ponta a ponta rodando o app instalado de verdade.**

## ⚠️ Leia isto antes de reinstalar o pacote MSIX para testar qualquer coisa

**`Remove-AppxPackage` antes de `Add-AppxPackage` apaga toda a pasta de dados do app** (banco SQLite,
API keys criptografadas via DPAPI) — já causou perda real da configuração de provider do usuário nesta
sessão (DEC-020). O procedimento correto agora é: incrementar o quarto componente da versão em
`src/TS4Translator.App/Package.appxmanifest` (`Version="1.0.X.0"`) e rodar só `Add-AppxPackage -Path $msix`
— **sem** `Remove-AppxPackage` antes — o que faz um upgrade in-place preservando os dados. Ver
`docs/BUILD.md` para o procedimento completo. `Remove-AppxPackage` só é necessário na transição única
de registro "solto" (`dotnet run`) para o primeiro pacote assinado — nunca depois disso.

**Importante sobre o estado atual:** o usuário pediu explicitamente para "concluir o projeto" antes de
fazer ele mesmo uma avaliação/teste completo no ambiente real, com uma rodada de correção separada para
qualquer problema que encontrar. Ele já testou ativamente contra um provider real (NVIDIA NIM),
encontrou bugs reais (DEC-017, DEC-018) que foram corrigidos, e depois — sem culpa dele, um erro do
processo de redeploy desta sessão (DEC-020) — perdeu a configuração de provider salva. Ele vai precisar
reconfigurar antes de continuar de onde parou.

## Current state

Ver `docs/STATUS.md` para o snapshot exato (atualizado junto com este arquivo). Resumo: fundação,
Sims4 Core, Translation Core (TokenProtector/TranslationValidator/BatchBuilder/TranslationOrchestrator),
Translation Memory exact-match, Project System, extração/persistência de strings
(`IProjectStringService`), configuração de provider com API key segura via DPAPI
(`IProviderConfigurationService`/`ISecretStore`), as quatro telas de produto (`HomePage`, `EditorPage`,
`SettingsPage`, `GlossaryPage`), e — nas rodadas mais recentes (DEC-012 a DEC-022) — edição manual
persistente com histórico, drag-and-drop de `.package` na Home com diff de reimportação, Glossário
(serviço + UI de escopo global), preset NVIDIA NIM em Configurações, Translation Profiles (instruções
de estilo livres por projeto), fuzzy matching em Translation Memory (Levenshtein, reaproveita ≥97%
similar sem chamar o provider, sempre marcado para revisão), correção de robustez contra tradução nula
(DEC-017), barra de progresso real + cancelamento seguro (DEC-018), **três providers reais**
(`OpenAICompatibleProvider`, `DeepLProvider`, `GoogleGeminiProvider`) selecionáveis via um
`ProviderKind` em Configurações (DEC-019), correção do model id padrão do Gemini que tinha ficado
obsoleto (DEC-021), e — a mais recente — **retry com backoff em falha transitória de provider**
(503/429/5xx/timeout, DEC-022) — tudo pronto e testado (202 testes automatizados + `GlossaryPage`/
preset NVIDIA NIM/barra de progresso/seletor de tipo de provider verificados rodando o app de verdade).

**O usuário já testou a tradução contra provider real** (NVIDIA NIM e, depois, Google Gemini) fora
desta sessão de desenvolvimento e encontrou vários problemas reais, todos corrigidos: `ArgumentNullException`
quando o provider respondia `"translation": null` para um item (DEC-017); falta de feedback de
progresso + salvamento só ao final numa tradução de ~25 mil strings (DEC-018); configuração de provider
apagada por um erro do processo de redeploy desta sessão (`Remove-AppxPackage` antes de reinstalar —
DEC-020, não recuperável, precisou reconfigurar); model id do Gemini (`gemini-2.0-flash`) descontinuado
pelo Google (DEC-021); e `HttpRequestException: 503 Service Unavailable` derrubando a tradução inteira
no meio de um mod real, mesmo com cota de sobra — corrigido com retry+backoff automático para falhas
transitórias (DEC-022). O usuário continua testando ativamente; qualquer novo problema relatado deve
virar uma correção pontual do mesmo jeito.

**Importante:** ao adicionar `customInstructions` (DEC-015), foi descoberto que o glossário (DEC-012)
nunca chegou a ser de fato usado pelo app real — `EditorPageViewModel.TranslateAsync` nunca buscava os
termos via `IGlossaryService` antes de chamar o orquestrador, apesar de toda a integração estar pronta
e testada em cada camada isoladamente. Já corrigido. Lição: um parâmetro opcional novo em
`ITranslationOrchestrator.TranslateAsync`/`ITranslationProvider.TranslateBatchAsync` não força o
compilador a atualizar call sites existentes — sempre fazer um grep manual por todo call site real
(não só os testes) depois de adicionar um.

## What has already been implemented

Ver a seção "Completed" de `docs/STATUS.md` — não duplicado aqui para evitar as duas listas
divergirem com o tempo. **Não recrie**: o pipeline de tradução inteiro, `IProjectStringService`,
`ISecretStore`/`IProviderConfigurationService`, `ITranslationProviderFactory`/`ConfiguredProviderFactory`,
`EditorPage`, `SettingsPage` (com seletor de `ProviderKind`), `IPackageService.WriteStringTablesAsync`
(múltiplas STBLs num package), `IGlossaryService`/`EfGlossaryService`, o parâmetro `glossaryTerms` em
`ITranslationProvider`/`ITranslationOrchestrator`, o histórico de revisão
(`ProjectStringRevisionRecord`/`GetHistoryAsync`), ou `DeepLProvider`/`GoogleGeminiProvider`.

## What is currently being worked on

Nada em progresso. Todo trabalho desta sessão foi levado a um estado compilável, testado, e
verificado rodando o app de verdade antes de parar.

## Exact next task

**O usuário precisa reconfigurar o provider primeiro** (config perdida no incidente da DEC-020) antes
de continuar testando a tradução real. Depois disso:

1. Vá em Configurações, escolha o tipo de provider no `ComboBox` ("OpenAI-compatible", "DeepL" ou
   "Google Gemini"), preencha Endpoint/Modelo (auto-preenchidos com um padrão sensato ao trocar de
   tipo, se o campo já não tiver um valor customizado) e a API key. Clique "Testar conexão".
2. Se a conexão funcionar, importe um `.package` (real ou sintético) e clique "Traduzir pendentes" no
   Editor. Confirme que as traduções aparecem e fazem sentido, e que `Status` reflete corretamente
   sucesso/revisão necessária.
3. Se algo falhar, o `StatusText` do Editor já mostra tipo+mensagem da exceção (`EditorPageViewModel.TranslateAsync`)
   — comece por aí antes de instrumentar mais.

`DeepLProvider` nunca rodou contra a API real — só testado com HTTP falso (DEC-019). Precisa de uma API
key para validar de fato. `GoogleGeminiProvider` já foi validado contra a API real com a chave do
usuário (DEC-021) — mas só depois de corrigir o model id padrão (`gemini-2.0-flash`), que a Google
tinha descontinuado em 2026-06-01; o padrão atual é `gemini-2.5-flash`, mas Google renomeia/descontinua
modelos com frequência, então isso pode ficar obsoleto de novo.

Depois de confirmar isso, o próximo item real de código é estender `GlossaryPage` (DEC-013) para
termos por projeto, não só globais — hoje ela só chama `IGlossaryService` com `projectId: null`. Isso
exigiria navegar até lá a partir do Editor passando `project.Id` como parâmetro (mesmo padrão de
`Frame.Navigate(typeof(EditorPage), project.Id)` já usado), e a página teria que aceitar um `Guid?`
opcional em `OnNavigatedTo`.

Depois disso, seguir a lista "Próximas tarefas concretas" no fim de `docs/ROADMAP.md`.

## Important files

- `docs/DECISIONS.md` — leia antes de mudar qualquer versão de pacote ou formato de arquivo; a
  maioria das escolhas não óbvias já foi justificada ali (DEC-001 a DEC-022).
- `docs/BUILD.md` — como compilar, testar, rodar, e (importante) como empacotar/instalar um MSIX
  local para testar qualquer fluxo que use `FileOpenPicker` ou outra API mediada por broker do
  Windows. `dotnet run` sozinho **não** funciona para esses casos (DEC-010) — não gaste tempo
  reinvestigando isso, o procedimento já está documentado e testado. **Para reinstalar uma build nova
  do MSIX: incrementar a versão em `Package.appxmanifest` e só `Add-AppxPackage`, nunca
  `Remove-AppxPackage` antes (DEC-020) — isso apaga os dados do app.**
- `src/TS4Translator.Core/Tokens/`, `src/TS4Translator.Translation/TokenProtector.cs` — a peça mais
  crítica de segurança do produto (seção 9 do brief). Não altere o design de reconciliação por
  identidade de placeholder sem entender DEC-004 primeiro.
- `src/TS4Translator.Sims4/SmartSimStringTableService.cs` — não reimplemente o bit de locale à mão;
  ver DEC-006 para o porquê.
- `src/TS4Translator.Persistence/Migrations/` — nunca edite uma migration manualmente depois que ela
  existir em qualquer branch compartilhado; adicione uma nova migration com `dotnet ef migrations
  add <Nome>` a partir de `src/TS4Translator.Persistence/`.
- `src/TS4Translator.App/ViewModels/HomePageViewModel.cs`/`EditorPageViewModel.cs`/`SettingsPageViewModel.cs`
  — note que toda chamada de I/O fica **dentro** do `try/catch` do método `[RelayCommand]`, de
  propósito (DEC-010) — `AsyncRelayCommand`/`[RelayCommand]` assíncrono não relança exceções do
  delegate por padrão, então qualquer código novo num command precisa do mesmo cuidado.
- `src/TS4Translator.App/ViewModels/EditorPageViewModel.cs` (`ExportAsync`) — nunca reporte
  exportação como sucesso sem a validação de round-trip (reabrir o arquivo gerado e comparar cada
  string) rodar primeiro. É o requisito da seção 28 do brief, e já está implementado — não remova.
- `../SimsPackageBatcher/` (fora deste repo, mesmo diretório pai) — projeto irmão já finalizado que
  prova o stack inteiro funciona neste ambiente; útil como referência de padrões (DI simples via
  `ServiceCollection`, uso de `LlamaLogic.Packages`, testes de integração com packages sintéticos)
  sempre que uma decisão de "como fazer isso em WinUI 3/.NET 10 neste ambiente" for necessária.

## Architectural decisions that must not be reverted

Todas em `docs/DECISIONS.md`, mas as mais fáceis de "corrigir" por engano:

- **Não** trocar o formato da solution de volta para `.slnx` sem resolver o `MSB4126` (DEC-002).
- **Não** remover a supressão de `CA1707` em `tests/` nem tentar "corrigir" nomes de teste com
  underscore — é intencional (DEC-003).
- **Não** reimplementar o cálculo de locale de STBL manualmente — usar `SmartSimUtilities` (DEC-006).
- **Não** reduzir a versão do EF Core/SQLite de volta para `10.0.0` — falha o NuGet audit por
  vulnerabilidades reais (DEC-007). Sempre usar a versão de patch mais recente disponível.
- **Não** fazer o `TokenProtector.Restore` confiar em ordem/contagem de tokens em vez de identidade de
  placeholder — é o ponto central do design (DEC-004) e existe teste específico para isso
  (`Restore_ReorderedTokens_IsValidButFlagged`, `Restore_DuplicatedToken_...`).
- **Não** assumir que `dotnet run` é suficiente para testar qualquer fluxo com `FileOpenPicker`/
  `FileSavePicker`/outra API de broker — não é (DEC-010). Use o procedimento de `docs/BUILD.md`.
- **Não** reintroduzir a ideia de que o problema do picker era elevação de privilégios — foi
  descartada por teste real (o usuário reproduziu o mesmo erro sem elevação nenhuma). A causa
  confirmada é o modo de registro de pacote do `dotnet run` (ver DEC-010).
- **Não** deixar a coluna de tradução do Editor editável sem persistir a edição (`UpdateTranslationAsync`)
  — um campo que edita e não salva é pior do que não ter o campo (DEC-011; já resolvido em DEC-012).
- **Não** fazer o `TranslationOrchestrator` enviar o glossário inteiro a cada chamada de provider — só
  os termos cujo `GlossaryTerm.AppearsIn(text)` bate com o batch específico (DEC-012, seção 23 do brief).
- **Não** usar `Binding ElementName=...` para um botão dentro de um `ItemTemplate` de `ListView` chamar
  um comando do ViewModel da página — não resolve de forma confiável em WinUI 3 (DEC-013, descoberto
  rodando o app de verdade). Usar `Tag="{x:Bind}"` + `Click` no code-behind, como `GlossaryPage`/
  `EditorPage` já fazem.
- **Não** rodar `Remove-AppxPackage` antes de `Add-AppxPackage` para atualizar o pacote instalado —
  apaga a pasta de dados do app inteira (DEC-020, causou perda de dados real). Incrementar a versão em
  `Package.appxmanifest` e usar só `Add-AppxPackage` para um upgrade in-place.
- **Não** deixar `HasApiKey`/qualquer flag parecida em `ProviderConfiguration` ser só copiada do
  registro anterior quando um novo `ProviderKind` é salvo — sempre recalcular a partir do que
  `ISecretStore` realmente tem para aquele `Kind` específico (DEC-019, bug real pego por um teste).

## Known bugs

Nenhum bug conhecido no código existente. Bugs reais encontrados em sessões anteriores já foram
corrigidos e commitados (exceção engolida pelo `AsyncRelayCommand`; o "bug" do picker era um problema
de processo de teste, não de código — ver DEC-010; `ArgumentNullException` quando o provider retornava
`"translation": null` — ver DEC-017; tradução de mods grandes sem feedback de progresso e sem
salvamento incremental — ver DEC-018; `HasApiKey` herdando o valor de um `ProviderKind` diferente ao
trocar de tipo — ver DEC-019). Um incidente de **perda de dados** (não um bug de código, mas um erro de
processo desta sessão) apagou a configuração de provider do usuário — ver DEC-020, já corrigido para
não repetir. Ver "Known issues" em `docs/STATUS.md` para riscos documentados que não são bugs
confirmados.

## Tests currently failing

Nenhum. 202 testes automatizados passando.

## Commands to build / test / run

Ver `docs/BUILD.md` — não duplicado aqui.

## Next milestones

Ver `docs/ROADMAP.md`. Prioridade imediata: usuário reconfigura o provider → testar `DeepLProvider`/
`GoogleGeminiProvider` contra as APIs reais → UI de glossário por projeto → UI de sugestões de fuzzy
match navegável → `TranslationJob`/progresso → `SettingsPage` em abas.

## Sobre o ambiente de desenvolvimento

O ambiente de execução usado para desenvolver **é** um desktop Windows 11 real do usuário —
`dotnet run` lança a janela do app de verdade, visível no desktop real, e o usuário pode (e já
conseguiu) interagir com ela diretamente. Foi possível verificar a UI de duas formas: pedindo para o
usuário testar manualmente, e via automação (Windows UI Automation / `System.Windows.Automation` em
PowerShell, mais `System.Drawing.Graphics.CopyFromScreen` para screenshots reais). **Não assuma que
"não há como ver a UI rodando"** — há, das duas formas.

**Cuidado ao rodar o app repetidamente durante desenvolvimento**: cada `dotnet run` (ou ativação via
AUMID de um pacote instalado) lança uma nova instância; instâncias antigas não fecham sozinhas. Sempre
`Stop-Process` a instância anterior antes de rebuildar (`Get-Process | Where-Object ProcessName -like
'*TS4Translator*'`), ou vai dar erro de arquivo em uso ao gerar um novo MSIX, ou vai ficar confuso qual
janela é qual.

**Sobre testar com um pacote MSIX instalado (`docs/BUILD.md`)**: isso registra o app de verdade no
Windows (`Add-AppxPackage`). Só desinstale (`Remove-AppxPackage`) quando for voltar de vez para
`dotnet run` — os dois modos de registro conflitam (`dotnet run` recusa rodar se já existir um pacote
instalado "de verdade" com a mesma identidade) e desinstalar apaga os dados do app (aceitável nesse
caso específico, já que o objetivo é sair do modo empacotado mesmo). **Enquanto continuar testando via
MSIX, nunca desinstale entre uma build e outra** — incremente a versão e use só `Add-AppxPackage`
(DEC-020). Não deixe o certificado de desenvolvimento (`ts4dev.pfx`) nem a pasta `AppPackages/` no
controle de versão — ambos já estão no `.gitignore`.
