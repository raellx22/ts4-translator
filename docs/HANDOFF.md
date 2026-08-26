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
desses**. Todo o código compila e a suíte de 81 testes automatizados passa. **Nada disso foi
verificado rodando o app de verdade** — ver a seção final deste arquivo antes de continuar.

## What is currently being worked on

Nada em progresso — a sessão anterior levou cada peça a um estado compilável e testado antes de parar.

## Exact next task

**Se você tem um desktop Windows real disponível agora, isso vem antes de qualquer código novo:**
rode `dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64` e confirme
visualmente que a janela abre, o Mica aparece, a navegação Home/About/Settings funciona, e que o botão
"Importar packages" na Home realmente abre um `FileOpenPicker` e, ao escolher um `.package`, cria um
projeto e mostra a contagem de STBLs sem travar. Anote qualquer coisa que quebrar em
`docs/STATUS.md` → "Known issues" antes de tentar consertar — pode ser um problema de uma linha (ex.:
um `x:Bind` com o `Mode` errado) ou pode revelar que o design do service locator (DEC-009) precisa
mudar. Depois disso, siga para a lista abaixo.

**Se você não tem acesso a um desktop Windows**, ou já fez a verificação acima, a próxima tarefa é:
Drag-and-drop de arquivos na `HomePage` (seção 34 pede isso explicitamente; hoje só existe o botão de
file picker) — usar os eventos `DragOver`/`Drop` do WinUI no `Grid` raiz da página, filtrando por
extensão `.package`, e chamar a mesma lógica de `HomePageViewModel.ImportPackagesAsync` mas recebendo
os caminhos do `DataPackageView` em vez do picker (talvez valha refatorar `ImportPackagesAsync` para
aceitar `IReadOnlyList<string>` diretamente e ter dois pontos de entrada — botão e drop — chamando-o).

Depois disso, seguir a lista "Próximas tarefas concretas" no fim de `docs/ROADMAP.md`.

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

## Uma coisa importante sobre o ambiente de desenvolvimento anterior

Esta sessão (e a sessão anterior a ela) rodaram sem acesso a um desktop Windows interativo (sem forma
de tirar screenshot da janela WinUI ou clicar nela). Tudo foi validado por `dotnet build`/`dotnet test`
na linha de comando — inclusive a `HomePage` inteira, com seu botão de importação e list view de
projetos recentes. **Isso significa que nenhuma tela WinUI deste app foi vista rodando de fato, nem
uma vez.** O compilador XAML do WinUI pega muitos erros de binding em tempo de build, mas não todos —
existe uma chance real de algo parecer certo aqui e falhar (ou simplesmente não fazer nada) ao clicar.
Trate a seção "Exact next task" acima como a prioridade número um se você tiver esse acesso.
