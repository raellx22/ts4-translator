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
divergirem com o tempo.

## What is currently being worked on

Nada em progresso — a sessão anterior levou cada peça a um estado compilável e testado antes de parar.

## Exact next task

Implementar `ITranslationProvider` em `TS4Translator.Core` (novo diretório `Core/Translation/`), com:

```csharp
public sealed record ProviderCapabilities(
    bool SupportsBatch, bool SupportsStreaming, bool SupportsCustomPrompt,
    bool SupportsGlossary, bool SupportsLocalExecution, bool RequiresApiKey, int? MaxContextSize);

public sealed record TranslationRequestItem(string Id, string ProtectedText);
public sealed record TranslationResultItem(string Id, string TranslatedProtectedText);

public interface ITranslationProvider
{
    string Name { get; }
    ProviderCapabilities Capabilities { get; }
    Task<IReadOnlyList<TranslationResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequestItem> items,
        CultureInfo sourceLocale, CultureInfo targetLocale,
        CancellationToken cancellationToken = default);
}
```

`TranslationRequestItem.Id`/`TranslationResultItem.Id` existem especificamente para a reconciliação
por ID exigida pela seção 10 do brief — nunca confiar na ordem/contagem dos resultados batendo com a
dos itens enviados. O `Id` deve ser algo que o chamador já controla (ex.: o `KeyHash` do STBL como
string, ou um GUID por item de batch), não gerado pelo provider.

Em seguida, criar `TS4Translator.Providers.Mock.MockTranslationProvider` (implementação trivial que
prefixa o texto com `"[mock] "`, sem rede) para poder testar o orquestrador de tradução sem depender
de nenhuma API externa — isso desbloqueia testes reais do `BatchBuilder`/orquestrador antes de
qualquer provider real (Argos/NVIDIA NIM) existir.

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
