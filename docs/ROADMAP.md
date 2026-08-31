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
  em `docs/ARGOS.md`.
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

- [x] `ITokenProtector`/`TokenProtector` — 27 casos de teste, incluindo quebras escapadas, URLs e
  handles, cobre todos os casos da seção 45.
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
- [x] Retry/backoff em falha transitória de rede (503/429/5xx/timeout) — `Providers/Resilience/HttpRetry.cs`,
  usado pelos três providers. Confirmado necessário por uma falha real (503 do Gemini derrubando a
  tradução inteira mesmo com cota de sobra). Ver DEC-022.
- [x] Baseline local de 8 GB (DEC-023) — Qwen3.5 4B Q4_K_M validado no Bionic/LM Studio; reasoning
  desligado, JSON Schema estrito, output cap, duas filas locais, translation memory e persistência em
  lote. Receita e medições em `docs/LOCAL_MODELS.md`.
- [x] Benchmark real e batches por provider (DEC-024) — WickedWhims com 6.779 strings; Qwen 32/3.500,
  Gemini 80/6.400, schema Gemini compatível e retry obedecendo o atraso de quota.
- [ ] Rate-limit proativo/pause/resume/checkpoint entre sessões do app (resto da seção 11) — não
  iniciado; depende de `TranslationJob` existir como entidade persistida (seção 35), o que também não
  existe ainda. O que já existe (DEC-018/DEC-022): checkpoint DENTRO de uma execução (blocos de 100
  persistidos incrementalmente) e retry automático de falha transitória — falta sobreviver a um
  fechamento do app no meio do caminho.
- [ ] `ITranslationJobService`/estados de job (seção 35) — não iniciado.

## FASE 4 — Argos — **implementada; falta certificação no MSIX final**

- [x] Runtime Python embarcável gerenciado pelo app, sem Python global.
- [x] Instalação enxuta Argos 1.11/CTranslate2 4.8.1 + modelo direto `en → pb`, com hashes fixados.
- [x] Sidecar persistente JSONL, UTF-8, ciclo de vida e erros gerenciados.
- [x] Provider/factory/DI e seleção/instalação/reparo na tela Configurações.
- [x] Batch CTranslate2 cruzando entradas e fallback determinístico de preservação de tokens.
- [x] Benchmark real WickedWhims: 500 entradas em 22,74 s, 496 aprovadas e 4 avisos não estruturais;
  execução integral das 6.779 strings em 4 min 28,6 s, sem ausência ou erro estrutural.
- [ ] Instalar o MSIX final em um Windows limpo e confirmar que o pacote consegue iniciar o executável
  baixado em LocalAppData; só então marcar a distribuição Argos como certificada.

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

## FASE 6 — Providers adicionais — **três providers reais (OpenAI-compatible/DeepL/Gemini); OpenAI dedicado e Claude não iniciados**

- [x] `OpenAICompatibleProvider` (seção 17) — `TS4Translator.Providers/OpenAICompatible/`. Um
  `POST /chat/completions` por batch, recuperação controlada de JSON truncado/malformado (seção 46),
  15 testes com HTTP falso. Serve tanto LM Studio local quanto NVIDIA NIM, já que ambos falam o mesmo
  protocolo (validado de verdade contra NVIDIA NIM pelo usuário, fora desta sessão — ver DEC-017/018).
- [x] `DeepLProvider` (seção 18) — `TS4Translator.Providers/DeepL/`. API dedicada de tradução (não um
  chat model), reconcilia resultado por posição no array (não por id), sem suporte a glossário/prompt
  customizado (DeepL não tem esse conceito nativo — glossário próprio da DeepL e `tag_handling=xml`
  ficam como melhoria futura). 12 testes com HTTP falso. **Nunca testado contra a API real.**
- [x] `GoogleGeminiProvider` (seção 18) — `TS4Translator.Providers/GoogleGemini/`. Mesma estratégia de
  prompt+recuperação de JSON do `OpenAICompatibleProvider`, envelope `generateContent` do Gemini.
  13 testes com HTTP falso. **Nunca testado contra a API real.**
- [x] `ProviderKind`/`ConfiguredProviderFactory` (DEC-019) — Configurações tem um seletor de tipo de
  provider (não mais um único formulário fixo), cada tipo com sua própria API key namespaced no
  `ISecretStore`. Ver DEC-019 para o design completo.
- [ ] Detecção automática de modelos via `GET /v1/models` (seção 17) — não iniciado, não bloqueia o
  provider funcionar com um model id digitado manualmente.
- [ ] OpenAI (API dedicada, não via endpoint compatível), Claude — não iniciados.

## FASE 7 — Translation Memory — **exact match concluído, fuzzy não iniciado**

- [x] `ITranslationMemory`/`SqliteTranslationMemory` — exact match por hash SHA-256 do texto
  normalizado + par de locales, com upsert. 10 testes de integração contra SQLite real.
- [x] Fuzzy matching (seção 26) — `StringSimilarity` (Levenshtein) + `FindSimilarAsync`, usado pelo
  orquestrador com limiar conservador (97%) para reaproveitar automaticamente uma tradução quase
  idêntica sem chamar o provider, sempre marcando para revisão. Ver DEC-016. Falta uma UI de
  "sugestões" navegável usando o limiar mais permissivo (85%) já exposto pela API.
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
- [x] Fuzzy matching de string similar-mas-não-idêntica no pipeline de tradução (seção 26) — ver Fase 7
  acima e DEC-016. O diff de reimportação (`ImportDiffResult`) em si continua só por hash exato: uma
  pequena correção de texto no mod ainda conta como "alterada" no diff (correto - o texto de fato
  mudou), mas a tradução dessa string alterada agora pode se beneficiar do fuzzy match ao traduzir.
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

1. **Reconfigurar o provider NVIDIA NIM** (config perdida no incidente da DEC-020) e retomar o teste
   real de tradução de onde parou. `GoogleGeminiProvider` já foi validado contra a API real (DEC-021).
2. **Testar `DeepLProvider` contra a API real** (DEC-019) — implementado e testado com HTTP falso, mas
   nunca rodou contra o servidor de verdade.
3. UI de glossário por projeto (`GlossaryPage` — DEC-013 — cobre só o escopo global hoje; termos
   específicos de um projeto exigiriam passar o `projectId` como parâmetro de navegação, igual ao
   Editor).
4. `ProviderConfiguration` com suporte a múltiplos perfis salvos simultâneos (um NVIDIA NIM e um DeepL
   configurados ao mesmo tempo, por exemplo, trocando qual está ativo), não só uma configuração ativa
   por vez (hoje trocar de `ProviderKind` substitui a anterior).
5. UI de "sugestões de fuzzy match" navegável no Editor (a API já existe com limiar permissivo de 85% —
   DEC-016 — só falta a tela; hoje o único consumo automático é o limiar conservador de 97% do
   orquestrador).
6. `AddHttpClient`/`IHttpClientFactory` em `TS4Translator.Infrastructure` — hoje `HttpClient` é um
   singleton simples registrado direto no `App`; não crítico, mas o padrão correto para um app que já
   tem três providers HTTP.
7. Entidade `TranslationJob` persistida (seção 35) — "Traduzir pendentes" já processa em blocos de 100
   com persistência incremental e cancelamento (DEC-018), mas o progresso não sobrevive a um fechamento
   do app (não há "retomar de onde parou" entre sessões, só dentro da mesma execução).
8. `SettingsPage` reorganizada em abas reais (General/Translation/Providers/Argos/NVIDIA NIM/Models/Appearance/Advanced,
   seção 41) e seletor de tema claro/escuro/sistema explícito.
