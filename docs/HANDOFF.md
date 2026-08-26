# Handoff

Leia isto antes de qualquer outra coisa se você é uma nova sessão/agente retomando este projeto.
Depois leia `README.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`,
`docs/BUILD.md`, nessa ordem. Não reconstrua a arquitetura do zero — ela existe, compila, tem 101
testes automatizados passando, e **o caminho prático completo (importar → extrair → editor →
exportar com validação) foi verificado de ponta a ponta rodando o app instalado de verdade.**

## Current state

Ver `docs/STATUS.md` para o snapshot exato (atualizado junto com este arquivo). Resumo: fundação,
Sims4 Core, Translation Core (TokenProtector/TranslationValidator/BatchBuilder/TranslationOrchestrator),
Translation Memory exact-match, Project System, extração/persistência de strings
(`IProjectStringService`), configuração de provider com API key segura via DPAPI
(`IProviderConfigurationService`/`ISecretStore`), e as três telas de produto (`HomePage`, `EditorPage`,
`SettingsPage`) — tudo pronto e testado. **A única peça do caminho principal ainda não exercitada com
dados reais é a chamada HTTP de tradução em si** (nunca rodou contra um LM Studio ou NVIDIA NIM de
verdade, só contra HTTP falso em teste e contra o fallback "sem provider configurado" no app real).

## What has already been implemented

Ver a seção "Completed" de `docs/STATUS.md` — não duplicado aqui para evitar as duas listas
divergirem com o tempo. **Não recrie**: o pipeline de tradução inteiro, `IProjectStringService`,
`ISecretStore`/`IProviderConfigurationService`, `ITranslationProviderFactory`, `EditorPage`,
`SettingsPage`, ou `IPackageService.WriteStringTablesAsync` (múltiplas STBLs num package).

## What is currently being worked on

Nada em progresso. Todo trabalho desta sessão foi levado a um estado compilável, testado, e
verificado rodando o app de verdade antes de parar.

## Exact next task

**Testar a tradução com um provider real.** Isso não é uma tarefa de código — é uma verificação que
precisa de infraestrutura externa (um LM Studio rodando localmente com um modelo carregado, ou uma
API key da NVIDIA NIM):

1. Rode o app (via `dotnet run` é suficiente — `SettingsPage`/`EditorPage` não usam `FileOpenPicker`,
   só o import na `HomePage` precisa do procedimento MSIX de `docs/BUILD.md`).
2. Vá em Configurações, preencha Endpoint (`http://localhost:1234/v1` para LM Studio, ou
   `https://integrate.api.nvidia.com/v1` para NVIDIA NIM) e Model (o nome exato do modelo carregado, ou
   `nvidia/riva-translate-4b-instruct-v2` para NIM), e a API key se for NIM. Clique "Testar conexão".
3. Se a conexão funcionar, importe um `.package` (real ou sintético) e clique "Traduzir pendentes" no
   Editor. Confirme que as traduções aparecem e fazem sentido, e que `Status` reflete corretamente
   sucesso/revisão necessária.
4. Se algo falhar, o `StatusText` do Editor já mostra tipo+mensagem da exceção (`EditorPageViewModel.TranslateAsync`)
   — comece por aí antes de instrumentar mais.

Depois de confirmar isso, o próximo item real de código é drag-and-drop de arquivos na `HomePage`
(brief seção 34 — hoje só existe o botão de file picker). Usar os eventos `DragOver`/`Drop` do WinUI
no `Grid` raiz de `Pages/HomePage.xaml`, filtrando por `.package`, reaproveitando a lógica de
`HomePageViewModel.ImportPackagesAsync` (considere extrair o corpo do método para aceitar
`IReadOnlyList<string>` diretamente, com dois pontos de entrada — botão e drop). Isso **não** precisa
do procedimento MSIX para testar — `dotnet run` é suficiente, drag-and-drop não passa por um broker.

Depois disso, seguir a lista "Próximas tarefas concretas" no fim de `docs/ROADMAP.md`.

## Important files

- `docs/DECISIONS.md` — leia antes de mudar qualquer versão de pacote ou formato de arquivo; a
  maioria das escolhas não óbvias já foi justificada ali (DEC-001 a DEC-011).
- `docs/BUILD.md` — como compilar, testar, rodar, e (importante) como empacotar/instalar um MSIX
  local para testar qualquer fluxo que use `FileOpenPicker` ou outra API mediada por broker do
  Windows. `dotnet run` sozinho **não** funciona para esses casos (DEC-010) — não gaste tempo
  reinvestigando isso, o procedimento já está documentado e testado.
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
  — um campo que edita e não salva é pior do que não ter o campo (DEC-011).

## Known bugs

Nenhum bug conhecido no código existente. Bugs reais encontrados em sessões anteriores já foram
corrigidos e commitados (exceção engolida pelo `AsyncRelayCommand`; o "bug" do picker era um problema
de processo de teste, não de código — ver DEC-010). Ver "Known issues" em `docs/STATUS.md` para riscos
documentados que não são bugs confirmados.

## Tests currently failing

Nenhum. 101 testes automatizados passando.

## Commands to build / test / run

Ver `docs/BUILD.md` — não duplicado aqui.

## Next milestones

Ver `docs/ROADMAP.md`. Prioridade imediata: testar a tradução com um provider real → drag-and-drop na
`HomePage` → edição manual persistente no Editor → filtros/pesquisa no Editor.

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
Windows (`Add-AppxPackage`). Sempre desinstale (`Remove-AppxPackage`) depois de terminar de testar,
antes de voltar para `dotnet run` — os dois modos de registro conflitam (`dotnet run` recusa rodar se
já existir um pacote instalado "de verdade" com a mesma identidade). Não deixe o certificado de
desenvolvimento (`ts4dev.pfx`) nem a pasta `AppPackages/` no controle de versão — ambos já estão no
`.gitignore`.
