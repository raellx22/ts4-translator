# Handoff

Leia isto antes de qualquer outra coisa se você é uma nova sessão/agente retomando este projeto.
Depois leia `README.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`,
nessa ordem. Não reconstrua a arquitetura do zero — ela existe, compila e tem 51 testes passando.

## Current state

Ver `docs/STATUS.md` para o snapshot exato (atualizado junto com este arquivo). Resumo: fundação da
solution pronta, camada Sims4 (STBL/locale) pronta com testes reais, TokenProtector e
TranslationValidator prontos com testes, translation memory (exact-match) pronta com SQLite real.
Nenhum provider de tradução, nenhuma UI de produto, nenhuma tela além do shell padrão do template
WinUI existem ainda.

## What has already been implemented

Ver a seção "Completed" de `docs/STATUS.md` — não duplicado aqui para evitar as duas listas
divergirem com o tempo. Em particular: o pipeline de tradução inteiro (`ITranslationProvider`,
`IBatchBuilder`, `ITranslationOrchestrator`), o primeiro provider real (`OpenAICompatibleProvider`),
`IProjectService`, e uma primeira tela de produto (`HomePage`) já existem — **não recrie nenhum
desses**. Todo o código compila e a suíte de 81 testes automatizados passa.

**Isto já foi rodado de verdade, não só compilado** — esta sessão descobriu que o ambiente de execução
tem acesso a um desktop Windows real e conseguiu lançar o app, navegar entre páginas via UI Automation,
e tirar screenshots reais (ver DEC-010). A shell, o Mica, o tema, a navegação Home/About e o layout da
`HomePage` estão confirmados funcionando visualmente. Um bug real (exceção engolida pelo
`AsyncRelayCommand` por a chamada do picker estar fora do `try/catch`) foi encontrado e corrigido.

## What is currently being worked on

Nada em progresso, mas há um **bloqueio de verificação em aberto**, não uma tarefa de código: ver
"Exact next task" abaixo antes de escrever qualquer coisa nova em cima de `HomePage`.

## Exact next task

Diagnosticar `COMException (0x80004005) E_FAIL` do `FileOpenPicker.PickMultipleFilesAsync()` (DEC-010).
Ao clicar "Importar packages" na `HomePage`, essa exceção acontece toda vez — confirmado com clique
sintético real (`SendInput`/`mouse_event`, não só automação de acessibilidade). A hipótese mais forte
(não confirmada) é que a sessão de ferramentas usada para desenvolver roda como Administrador, e há
relatos públicos do WindowsAppSDK do mesmo HRESULT nessa situação. Uma tentativa de des-elevar via
`Shell.Application.ShellExecute` não funcionou (o processo filho continuou elevado — `TokenElevation`
ainda `1` depois).

**Se você tem uma forma de rodar o app publicado/instalado normalmente** (não via `dotnet run` de uma
sessão de ferramentas elevada) — por exemplo, o usuário testando manualmente no próprio desktop, fora
de qualquer sessão de automação — peça para clicar "Importar packages" e escolher um `.package`
qualquer. Se funcionar nessa condição, a causa era mesmo elevação, e a decisão correta é **não mudar
nada no código do app** (rodar sem privilégios de administrador já é o comportamento esperado e correto
de qualquer app desktop) — só remover a nota de bloqueio de `docs/STATUS.md` e `docs/DECISIONS.md` DEC-010.

Se ainda falhar sem elevação, aí sim investigar mais fundo: checar `Package.appxmanifest` (capabilities
declaradas), confirmar que `picker.FileTypeFilter.Add(".package")` está sendo chamado antes de
`PickMultipleFilesAsync()` (já está, mas há relatos de bugs do WindowsAppSDK sensíveis à ordem), e
testar `PickSingleFileAsync()` em vez de `PickMultipleFilesAsync()` para isolar se o problema é
específico de seleção múltipla (um dos achados da pesquisa desta sessão foi justamente um issue aberto
do WindowsAppSDK especificamente sobre `PickMultipleFilesAsync` quebrando em WinUI 3 — vale ler
https://github.com/microsoft/WindowsAppSDK/issues/467 antes de gastar mais tempo tentando reproduzir
do zero).

Depois de resolver isso, a próxima tarefa é drag-and-drop de arquivos na `HomePage` (seção 34 pede
isso explicitamente; hoje só existe o botão de file picker) — usar os eventos `DragOver`/`Drop` do
WinUI no `Grid` raiz da página, filtrando por extensão `.package`. Depois disso, seguir a lista
"Próximas tarefas concretas" no fim de `docs/ROADMAP.md`.

## Important files

- `docs/DECISIONS.md` — leia antes de mudar qualquer versão de pacote ou formato de arquivo; a
  maioria das escolhas não óbvias já foi justificada ali (DEC-001 a DEC-007).
- `src/TS4Translator.Core/Tokens/`, `src/TS4Translator.Translation/TokenProtector.cs` — a peça mais
  crítica de segurança do produto (seção 9 do brief). Não altere o design de reconciliação por
  identidade de placeholder sem entender DEC-004 primeiro.
- `src/TS4Translator.Sims4/SmartSimStringTableService.cs` — não reimplemente o bit de locale à mão;
  ver DEC-006 para o porquê.
- `src/TS4Translator.Persistence/Migrations/` — nunca edite `InitialCreate` manualmente depois que
  ela existir em qualquer branch compartilhado; adicione uma nova migration com `dotnet ef migrations
  add <Nome>` a partir de `src/TS4Translator.Persistence/`.
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

## Known bugs

Nenhum bug conhecido no código existente — ver "Known issues" em `docs/STATUS.md` para riscos
documentados (não são bugs confirmados, são pontos não testados contra dados reais).

## Tests currently failing

Nenhum.

## Commands to build / test / run

Ver `docs/STATUS.md`, seções correspondentes — não duplicado aqui.

## Next milestones

Ver `docs/ROADMAP.md`. Prioridade imediata: `ITranslationProvider` → `BatchBuilder`/orquestrador →
`Project`/`IProjectService` → primeira tela de produto real na UI.

## Uma coisa importante sobre o ambiente de desenvolvimento

Ao contrário do que sessões anteriores a esta assumiam, o ambiente de execução usado para desenvolver
**é** um desktop Windows 11 real do usuário — `dotnet run` lança a janela do app de verdade, visível
no desktop real. Foi possível verificar isso interagindo com a janela via Windows UI Automation
(`System.Windows.Automation` em PowerShell) e capturando screenshots reais via
`System.Drawing.Graphics.CopyFromScreen`. **Não assuma mais que "não há como ver a UI rodando"** — há.
O que ainda não existe é uma forma automática/roteirizada de fazer isso (nenhum tool dedicado, tudo foi
feito com PowerShell ad-hoc), então cada verificação visual futura vai precisar do mesmo tipo de script
manual — ver os comandos usados nesta sessão no histórico de commits/PR se precisar do padrão exato.

**Cuidado ao rodar o app repetidamente durante desenvolvimento**: cada `dotnet run` lança uma nova
instância via AUMID; instâncias antigas não são fechadas automaticamente. Sempre `Stop-Process` a
instância anterior (por `Id`, obtido de `Get-Process | Where-Object ProcessName -like '*TS4Translator*'`)
antes de rebuildar, ou os testes manuais vão ficar confusos sobre qual janela é qual.

**A sessão de ferramentas usada para desenvolver roda como Administrador** (confirmado via token de
elevação). Isso pode causar falhas específicas de APIs WinRT mediadas por broker (como
`FileOpenPicker` — ver DEC-010) que não aconteceriam para um usuário final rodando o app normalmente.
Ao investigar um bug que envolve picker, diálogo do sistema, notificação, ou qualquer outra API que
passe por um broker do Windows, considere elevação como suspeita antes de assumir que é um bug real
do app.
