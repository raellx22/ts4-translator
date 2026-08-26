# Roadmap

Fases conforme o brief original do projeto (seção 57). Cada fase lista o que já está feito e o que
falta. Não pule uma fase só porque uma peça isolada dela já existe — o critério de "fase concluída" é
o conjunto de itens, não o primeiro que funcionar.

## FASE 0 — Pesquisa técnica — **concluída**

- [x] Stack confirmado por evidência direta (projeto irmão `SimsPackageBatcher` já compilado e testado
  com o mesmo conjunto de versões) — DEC-001.
- [x] LlamaLogic.Packages avaliado e confirmado: STBL via `StringTableModel`, locale via
  `SmartSimUtilities` — DEC-006, verificado com testes reais, não apenas lido em documentação.
- [x] Argos Translate pesquisado: sem `.exe` oficial, mas sem dependência de `torch` na versão atual
  (`ctranslate2`+`spacy`+`sacremoses`, wheels Windows nativos disponíveis); prior art .NET encontrado
  (`ArgosTranslate.PyNet`, via pythonnet). Ver relatório de pesquisa arquivado nesta sessão — resumo
  em `docs/ARGOS.md` (a criar na Fase 4).
- [x] NVIDIA NIM pesquisado: endpoint compatível com OpenAI (`/v1/chat/completions`), modelo
  `nvidia/riva-translate-4b-instruct-v2` confirmado atual, template de prompt observado ao vivo no
  playground (system prompt em linguagem natural, não tag curta — discrepância encontrada com a doc
  do NGC catalog, tratar como não confirmada até testar com uma API key real).

## FASE 1 — Fundação — **quase concluída**

- [x] Solution, projetos, DI (`ServiceCollection` simples, não Generic Host — DEC-007), logging não
  implementado ainda.
- [x] SQLite com migrations reais (`TranslatorDbContext`, `InitialCreate`).
- [x] Estrutura de documentação (este diretório).
- [ ] Logging estruturado (seção 36) — não iniciado.
- [x] Primeira tela de produto real (Home: importar `.package`, criar projeto, listar recentes) —
  fluxo completo confirmado funcionando de ponta a ponta rodando o app instalado como MSIX (DEC-010).
- [ ] Tela de Settings com abas reais (General/Translation/Providers/Argos/NVIDIA NIM/Models/Appearance/Advanced,
  seção 41) — hoje é o placeholder do template.
- [ ] Seletor de tema claro/escuro/sistema explícito na UI (Mica já funciona via `MicaBackdrop` do
  template, mas não há toggle de tema).
- [ ] Estratégia de localização da UI (seção 41 — "arquiteturalmente localizável mesmo que só exista
  PT-BR hoje") — strings ainda estão hardcoded em inglês no template gerado.

## FASE 2 — The Sims 4 Core — **núcleo concluído, falta escala**

- [x] Package reader/writer, enumeração de recursos, extração/escrita de STBL, locale handling —
  `Sims4PackageService`/`SmartSimStringTableService`, 11 testes de round-trip.
- [ ] Testes com um `.package` real de um mod (hoje só packages sintéticos, sem conteúdo de terceiros
  no repo — seção 44). Validar contra um mod real fica pendente até termos um fixture apropriado.
- [ ] Modelo de "análise de mod" (contagem de strings, STBLs encontradas) para alimentar a UI de
  importação (seção 34) — não iniciado.
- [ ] Detecção/tratamento de package inválido ou corrompido (seção 27/46) na camada Sims4 — não iniciado.

## FASE 3 — Translation Core — **em andamento, núcleo pronto**

- [x] `ITokenProtector`/`TokenProtector` — 23 testes, cobre todos os casos da seção 45.
- [x] `ITranslationValidator`/`TranslationValidator` — cobre vazio, idêntico, discrepância de tamanho,
  markup desbalanceado, e funde os problemas de token.
- [x] `ITranslationProvider`/`ProviderCapabilities` (seção 12) — contrato em `Core/Translation/`.
- [x] `IBatchBuilder`/`BatchBuilder` (seção 11) — greedy fill por contagem e por total de caracteres;
  um item sozinho maior que o limite vira seu próprio batch em vez de ser cortado. 6 testes.
- [x] `ITranslationOrchestrator`/`TranslationOrchestrator` — pipeline completo: translation memory →
  protect → provider → reconciliação por ID → restore → validate → grava na memory. Reconciliação por
  ID testada explicitamente contra resultado ausente e resultado duplicado do provider (seção 10). 8 testes.
- [x] Glossário (seção 23) — `IGlossaryService`/`EfGlossaryService`, termos globais ou por projeto,
  `NeverTranslate`, propagado por `ITranslationOrchestrator`/`ITranslationProvider` e filtrado por
  batch (só termos relevantes ao lote são enviados). `OpenAICompatibleProvider` inclui o glossário no
  prompt. Ver DEC-012. `GlossaryPage` cobre o escopo global (DEC-013); termos por projeto ainda não
  têm UI dedicada.
- [x] Translation Profiles (seção 22, escopo: instruções livres por projeto, não uma biblioteca de
  presets nomeados) — `TranslationProject.CustomInstructions`, editável na Editor, propagado ao
  orquestrador/provider igual ao glossário. `TranslationMemoryEntry.ProfileName` continua `null` (não
  há um nome de perfil reutilizável, só texto livre por projeto) — ver DEC-015 para o porquê do escopo.
- [ ] Retry/backoff/rate-limit/pause/resume/checkpoint (seção 11, resto) — não iniciado; depende de
  `TranslationJob` existir como entidade persistida (seção 35), o que também não existe ainda.
- [ ] `ITranslationJobService`/estados de job (seção 35) — não iniciado.

## FASE 4 — Argos — **não iniciada**

Ver achados de pesquisa: sem dependência de `torch`, mas nenhuma receita testada de empacotamento
"Python embutido + argostranslate" foi encontrada como prior art — vai exigir prototipagem própria.
Avaliar `ArgosTranslate.PyNet` (pythonnet) como alternativa a um sidecar via stdin/stdout antes de
comprometer a arquitetura com uma das duas abordagens.

## FASE 5 — NVIDIA NIM — **base pronta via `OpenAICompatibleProvider`, falta validar e expor na UI**

O endpoint da NVIDIA é compatível com OpenAI, então `OpenAICompatibleProvider` (Fase 6, já
implementado) já deveria funcionar apontando para `https://integrate.api.nvidia.com/v1` com
`Model = "nvidia/riva-translate-4b-instruct-v2"` e uma API key — isso nunca foi testado contra a API
real nesta sessão (sem chave disponível). Antes de expor isso como "preset Riva Translate" na UI:
1. Validar empiricamente com uma API key real qual template de prompt o v2 realmente espera
   (nosso system prompt genérico de tradução vs. a possibilidade de um formato tag curta
   `en-pt_BR` — as duas fontes pesquisadas na Fase 0 discordam sobre isso).
2. Confirmar se `temperature=0` é aceito e produz saída estável (não documentado pela NVIDIA).
3. [x] Preset de endpoint/model para o caso Riva Translate — `SettingsPageViewModel.ApplyNvidiaNimPresetCommand`
   (DEC-014). Preenche os campos, não salva sozinho, e não afirma que o formato de prompt foi validado
   — os itens 1 e 2 acima continuam pendentes de uma API key real.

## FASE 6 — Providers adicionais — **`OpenAICompatibleProvider` concluído; os demais não iniciados**

- [x] `OpenAICompatibleProvider` (seção 17) — `TS4Translator.Providers/OpenAICompatible/`. Um
  `POST /chat/completions` por batch, recuperação controlada de JSON truncado/malformado (seção 46),
  9 testes com HTTP falso. Serve tanto LM Studio local quanto (em teoria, não validado) NVIDIA NIM,
  já que ambos falam o mesmo protocolo.
- [ ] Detecção automática de modelos via `GET /v1/models` (seção 17) — não iniciado, não bloqueia o
  provider funcionar com um model id digitado manualmente.
- [ ] DeepL, Google, OpenAI (API dedicada, não via endpoint compatível), Gemini, Claude — não iniciados.

## FASE 7 — Translation Memory — **exact match concluído, fuzzy não iniciado**

- [x] `ITranslationMemory`/`SqliteTranslationMemory` — exact match por hash SHA-256 do texto
  normalizado + par de locales, com upsert. 5 testes de integração contra SQLite real.
- [ ] Fuzzy matching (seção 26) — não iniciado.
- [ ] Ligação com projeto/versão de mod: `Project` já existe (ver Fase 1/33 abaixo); `ModVersion`
  (para o diff de versões da Fase 8) ainda não.

## FASE 33 (Project System, referenciada pela Fase 1) — **núcleo pronto**

- [x] `IProjectService`/`EfProjectService` — criar, listar recentes (ordenado por atualização mais
  recente, client-side por causa de DEC-008), buscar por ID, deletar (cascade nos arquivos-fonte).
  7 testes de integração contra SQLite real.
- [ ] Ligação com strings/traduções extraídas (ainda não existe uma entidade `StringEntry` — um
  projeto hoje só sabe seus arquivos-fonte e seu par de locales, não suas strings).
- [ ] Atualização de `UpdatedAtUtc` em qualquer operação futura que modifique o projeto (import,
  tradução, exportação) — `EfProjectService` não faz isso automaticamente hoje, só `CreateAsync` seta
  o timestamp inicial.

## FASE 8 — Mod Update Engine — **groundwork pronto, fuzzy matching não iniciado**

- [x] Diff de reimportação (`ImportDiffResult`: novo/alterado/inalterado/removido) calculado em
  `EfProjectStringService.ReplaceStringsAsync` e reportado na Home após importar/reimportar. Uma
  tradução existente cujo texto de origem mudou é marcada `NeedsReview`, não descartada. Ver DEC-012.
- [ ] Fuzzy matching de string similar-mas-não-idêntica (seção 25/26) — hoje a comparação é só por hash
  exato; uma pequena correção de texto no mod original é tratada como "mudou completamente", não como
  "quase igual, reaproveitar com revisão".
- [ ] `ModVersion`/histórico de versões do mod importado — não iniciado.

## FASE 9 — Editor — **completa para o núcleo pedido, painel lateral não iniciado**

- [x] `EditorPage`/`EditorPageViewModel` — lista virtualizada (original, tradução, status) por
  projeto, botão "Traduzir pendentes" (usa `ITranslationOrchestrator`), navegação a partir da lista
  de projetos recentes na Home. Confirmado rodando o app instalado (DEC-011).
- [x] Filtros (Todas/Pendentes/Traduzidas/Revisar/Editadas manualmente — seção 30) e pesquisa por texto
  (original + tradução) na lista de strings. Ver DEC-012.
- [x] Edição manual persistente — coluna de tradução editável, salva em `LostFocus` via
  `UpdateTranslationAsync(..., ProjectStringStatus.ManuallyEdited, ...)`, com histórico simples
  (`ProjectStringRevisionRecord`/`GetHistoryAsync`, seção 31). Ver DEC-012.
- [ ] Painel lateral com tokens/glossário/histórico por string (seção 30) — `GetHistoryAsync` já existe
  no serviço, falta só a UI de exibição; não iniciado.

## FASE 10 — Hybrid Translation — **não iniciada**

## FASE 11 — Export — **núcleo pronto e verificado de ponta a ponta**

- [x] `EditorPageViewModel.ExportAsync` — agrupa as strings do projeto por STBL de origem, troca o
  locale via `IStringTableService.WithLocale`, escreve um único package via
  `IPackageService.WriteStringTablesAsync`, e só reporta sucesso depois de reabrir o arquivo e
  comparar cada string com o esperado (round-trip real, seção 28). Confirmado gerando e validando um
  `.package` de verdade rodando o app instalado (DEC-011).
- [ ] Relatório de exportação mais detalhado (contagem de avisos/erros por string, não só
  sucesso/falha geral) — não iniciado.
- [ ] Backup automático / opção de incorporar ao package original (seção 7, sempre opcional e nunca
  padrão) — não iniciado; hoje só existe o caminho "package separado", que é o exigido como padrão.

## FASE 12 — UX/Polish — **não iniciada**

## FASE 13 — Hardening — **não iniciada**

## FASE 14 — Release — **não iniciada**

---

## Próximas tarefas concretas (em ordem sugerida)

1. **Testar a tradução com um provider real** (LM Studio local ou NVIDIA NIM com API key) — todo o
   pipeline foi verificado com o fallback "sem provider" e com HTTP falso em teste, mas o caminho
   HTTP real de ponta a ponta dentro do app (`SettingsPage` → salvar config → `EditorPage` → "Traduzir
   pendentes" → resposta real chegando) nunca rodou. Ver `docs/HANDOFF.md`.
2. UI de glossário por projeto (`GlossaryPage` — DEC-013 — cobre só o escopo global hoje; termos
   específicos de um projeto exigiriam passar o `projectId` como parâmetro de navegação, igual ao
   Editor).
3. `ProviderConfiguration` com suporte a múltiplos perfis salvos (LM Studio + NVIDIA NIM ao mesmo
   tempo, por exemplo), não só um endpoint ativo por vez.
4. Fuzzy matching em Translation Memory e no diff de reimportação (seção 25/26) — hoje ambos são só
   por hash exato.
5. `AddHttpClient`/`IHttpClientFactory` em `TS4Translator.Infrastructure` — hoje `HttpClient` é um
   singleton simples registrado direto no `App`; não crítico, mas o padrão correto para um app que vai
   ganhar mais providers HTTP.
6. Entidade `TranslationJob` (seção 35) — hoje "Traduzir pendentes" roda tudo de uma vez, sem
   checkpoint/retomada; para mods grandes (milhares de strings) isso vai precisar de progresso
   persistido, não só uma chamada síncrona bloqueante.
7. `SettingsPage` reorganizada em abas reais (General/Translation/Providers/Argos/NVIDIA NIM/Models/Appearance/Advanced,
   seção 41) e seletor de tema claro/escuro/sistema explícito.
