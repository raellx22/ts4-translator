# Handoff

Leia isto antes de qualquer outra coisa se você é uma nova sessão/agente retomando este projeto.
Depois leia `README.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`,
`docs/BUILD.md`, nessa ordem. Não reconstrua a arquitetura do zero — ela existe, compila e tem 81
testes automatizados passando, e a primeira tela de produto funciona de ponta a ponta de verdade.

## Current state

Ver `docs/STATUS.md` para o snapshot exato (atualizado junto com este arquivo). Resumo: fundação da
solution pronta; camada Sims4 (STBL/locale) pronta com testes reais; TokenProtector, TranslationValidator,
BatchBuilder e TranslationOrchestrator prontos com testes; translation memory (exact-match) pronta com
SQLite real; `IProjectService` pronto; `OpenAICompatibleProvider` pronto (nunca testado contra um
servidor real, só contra HTTP falso); e a `HomePage` (importar `.package` → contar STBLs → criar
projeto → listar recentes) **confirmada funcionando de ponta a ponta rodando o app de verdade**, não
só compilando.

## What has already been implemented

Ver a seção "Completed" de `docs/STATUS.md` — não duplicado aqui para evitar as duas listas
divergirem com o tempo. **Não recrie**: o pipeline de tradução (`ITranslationProvider`,
`IBatchBuilder`, `ITranslationOrchestrator`), o `OpenAICompatibleProvider`, o `IProjectService`, e a
`HomePage`.

## What is currently being worked on

Nada em progresso. Todo trabalho desta sessão foi levado a um estado compilável, testado, e — pela
primeira vez neste projeto — verificado rodando o app de verdade, antes de parar.

## Exact next task

Drag-and-drop de arquivos/pastas na `HomePage` (brief seção 34 pede isso explicitamente; hoje só
existe o botão "Importar packages" via file picker). Usar os eventos `DragOver`/`Drop` do WinUI no
`Grid` raiz de `Pages/HomePage.xaml`, filtrando por extensão `.package`, reaproveitando a mesma lógica
de `HomePageViewModel.ImportPackagesAsync` (talvez valha extrair o corpo do método para aceitar
`IReadOnlyList<string>` diretamente, com dois pontos de entrada — botão e drop — chamando essa lógica
compartilhada).

**Importante**: para testar isso manualmente (drag-and-drop, como qualquer outra interação de UI que
não passe por um broker do Windows), `dotnet run` funciona normalmente — não é necessário empacotar
como MSIX para isso. O empacotamento só é necessário para testar o `FileOpenPicker` (ver `docs/BUILD.md`).

Depois disso, seguir a lista "Próximas tarefas concretas" no fim de `docs/ROADMAP.md` — a prioridade
seguinte é a entidade `StringEntry`.

## Important files

- `docs/DECISIONS.md` — leia antes de mudar qualquer versão de pacote ou formato de arquivo; a
  maioria das escolhas não óbvias já foi justificada ali (DEC-001 a DEC-010).
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
- `src/TS4Translator.App/ViewModels/HomePageViewModel.cs` — note que a chamada ao picker está
  **dentro** do `try/catch`, de propósito (DEC-010) — `AsyncRelayCommand` não relança exceções do
  delegate por padrão, então qualquer código novo em um `AsyncRelayCommand` precisa do mesmo cuidado.
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

## Known bugs

Nenhum bug conhecido no código existente. Dois bugs reais encontrados nesta sessão já foram
corrigidos e commitados (exceção engolida pelo `AsyncRelayCommand`; o "bug" do picker era um problema
de processo de teste, não de código — ver DEC-010). Ver "Known issues" em `docs/STATUS.md` para riscos
documentados que não são bugs confirmados.

## Tests currently failing

Nenhum. 81 testes automatizados passando.

## Commands to build / test / run

Ver `docs/BUILD.md` — não duplicado aqui.

## Next milestones

Ver `docs/ROADMAP.md`. Prioridade imediata: drag-and-drop na `HomePage` → entidade `StringEntry` →
testar `OpenAICompatibleProvider` contra um LM Studio real → armazenamento seguro de API key.

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
