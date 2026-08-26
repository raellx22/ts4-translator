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
  nunca executada visualmente, ver nota em `docs/ARCHITECTURE.md`/`docs/HANDOFF.md`.
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
- [ ] Retry/backoff/rate-limit/pause/resume/checkpoint (seção 11, resto) — não iniciado; depende de
  `TranslationJob` existir como entidade persistida (seção 35), o que também não existe ainda.
- [ ] Translation Profiles (seção 22) — não iniciado; o orquestrador hoje não aceita nenhum prompt/estilo
  customizado, e `TranslationMemoryEntry.ProfileName` fica `null` em toda tradução nova.
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
3. Adicionar um preset/`ProviderConfiguration` (seção 37, ainda não implementado) em vez de exigir
   que o usuário digite endpoint/model manualmente para o caso Riva Translate.

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

## FASE 8 — Mod Update Engine — **não iniciada**

## FASE 9 — Editor — **não iniciada**

## FASE 10 — Hybrid Translation — **não iniciada**

## FASE 11 — Export — **não iniciada** (mas `IPackageService.WriteStringTableAsync` já cobre a
mecânica de escrita de package; falta orquestrar validação + round-trip + relatório em torno dela).

## FASE 12 — UX/Polish — **não iniciada**

## FASE 13 — Hardening — **não iniciada**

## FASE 14 — Release — **não iniciada**

---

## Próximas tarefas concretas (em ordem sugerida)

1. **Rodar o app de verdade.** Ver a nota no fim de `docs/HANDOFF.md` — nenhuma sessão até agora teve
   acesso a um desktop Windows interativo. `HomePage` foi escrita e compila, mas nunca foi clicada.
   Isso deveria ser a primeira ação de qualquer sessão com esse acesso, antes de construir mais em
   cima dela.
2. Drag-and-drop de arquivos/pastas na `HomePage` (seção 34 pede explicitamente, hoje só há o botão
   "Importar packages" via file picker) — e suporte a pasta inteira, não só múltiplos arquivos.
3. Entidade `StringEntry` (nova migration) — cada string extraída de um projeto, ligada ao `Project`
   e ao `KeyHash`/`StblKey` de origem. Pré-requisito para o editor (Fase 9) e para o diff de versões
   (Fase 8) existirem. A `HomePage` de hoje só conta STBLs, não extrai/persiste strings individuais.
4. Testar `OpenAICompatibleProvider` contra um LM Studio local de verdade — confirma que o pipeline
   inteiro (orchestrator → provider → HTTP real → parsing) funciona fora dos testes com HTTP falso.
5. Armazenamento seguro de API key (Windows Credential Manager/DPAPI, seção 19) — pré-requisito para
   testar `OpenAICompatibleProvider` contra NVIDIA NIM (que exige uma), e para qualquer provider pago
   futuro.
6. `AddHttpClient`/`IHttpClientFactory` em `TS4Translator.Infrastructure` — hoje quem instancia
   `OpenAICompatibleProvider` tem que criar o `HttpClient` manualmente; não crítico, mas deveria ser
   resolvido antes da UI de Settings existir de verdade (seção 41).
