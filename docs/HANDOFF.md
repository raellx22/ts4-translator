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
`IBatchBuilder`, `ITranslationOrchestrator`) e o primeiro provider real (`OpenAICompatibleProvider`,
`src/TS4Translator.Providers/OpenAICompatible/`) já existem e estão testados — **não recrie essas
interfaces nem esse provider**.

## What is currently being worked on

Nada em progresso — a sessão anterior levou cada peça a um estado compilável e testado antes de parar.

## Exact next task

Duas coisas de igual prioridade, escolha pela que fizer mais sentido dado o ambiente que você tem:

**Se você tem um desktop Windows com LM Studio (ou qualquer servidor compatível com OpenAI) rodando:**
teste `OpenAICompatibleProvider` contra ele de verdade — isso nunca foi feito, só contra HTTP falso em
teste. Aponte `OpenAICompatibleProviderOptions.Endpoint` para o servidor local, rode uma tradução
pequena via `TranslationOrchestrator`, e confirme que o formato de resposta do servidor real bate com
o que o parser espera. Registre o resultado (funcionou / o que precisou ajustar) em `docs/DECISIONS.md`.

**Caso contrário:** implemente a entidade `Project` em `TS4Translator.Persistence` (nova migration,
`dotnet ef migrations add AddProject` a partir de `src/TS4Translator.Persistence/`) + `IProjectService`
em `Core` (seção 33 do brief: nome, arquivos importados, idioma, target locale, provider, histórico).
Sem isso não há como a UI listar "projetos recentes" nem ligar strings/traduções a um mod específico —
é o pré-requisito bloqueando a primeira tela de produto real.

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

Esta sessão rodou sem acesso a um desktop Windows interativo (sem forma de tirar screenshot da janela
WinUI ou clicar nela). Tudo foi validado por `dotnet build`/`dotnet test` na linha de comando. **Isso
significa que a shell WinUI nunca foi vista rodando de fato.** Se você tem acesso a um desktop Windows
real, rode `dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64`
como primeira ação e confirme visualmente que a janela abre, o Mica aparece, e a navegação entre
Home/About/Settings funciona, antes de continuar construindo em cima dela.
