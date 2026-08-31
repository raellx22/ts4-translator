# Status

Última atualização: 2026-08-31.

## Current phase

O caminho prático completo já funciona de ponta a ponta, verificado rodando o app instalado de
verdade (não só compilando): **importar um `.package` → extrair e persistir cada string → abrir no
Editor → traduzir as pendentes (via um provider compatível com OpenAI configurado em Configurações) →
exportar um package traduzido com validação de round-trip real**. Ver DEC-011 para o relato completo
da verificação.

A chamada HTTP de tradução em si já foi exercitada pelo usuário contra um provider real (NVIDIA NIM,
fora desta sessão de desenvolvimento) e encontrou dois problemas reais:
1. `ArgumentNullException` quando o provider respondia `"translation": null` para um item, derrubando a
   tradução do batch inteiro. Corrigido (DEC-017): a resposta agora é filtrada na origem e o
   orquestrador tem uma defesa própria contra qualquer `ITranslationProvider` que devolva uma tradução
   nula.
2. Nenhum feedback de progresso real durante uma tradução de mod grande (~25 mil strings) — só um
   spinner indeterminado, e (mais sério) nada era salvo até a tradução inteira terminar, então qualquer
   interrupção no meio perdia 100% do trabalho já feito. Corrigido (DEC-018): tradução agora acontece
   em blocos de 100 strings, cada um persistido antes do próximo, com barra de progresso real e botão
   de cancelar (que preserva o que já foi traduzido).

O provider local foi validado de verdade em 2026-08-29 com Bionic/LM Studio e Qwen3.5 4B Q4_K_M numa
RTX 4060 Ti 8 GB: contexto 8.192, raciocínio desligado, saída estruturada e dois lotes simultâneos.
No WickedWhims real, 200 strings levaram 118,60 s (projeção ~67 min). Gemini 3.5 Flash-Lite preservou
estrutura em 200/200 com lote 80 e deixou 2 textos para revisão, mas o free tier observado limita 15 requests/min; ver DEC-024 e
`docs/LOCAL_MODELS.md`. Estrutura e placeholders foram validados; qualidade linguística ainda precisa
de avaliação humana.

**Importante:** a configuração de provider do usuário (endpoint, modelo e API key da NVIDIA NIM) foi
**perdida** num incidente real durante esta sessão — reinstalar o pacote MSIX (`Remove-AppxPackage` +
`Add-AppxPackage`) apaga a pasta de dados do app inteira, banco e segredos DPAPI inclusos. Corrigido o
procedimento de redeploy (DEC-020, `docs/BUILD.md`) para nunca mais remover antes de reinstalar, mas a
configuração antiga não tinha como ser recuperada — o usuário precisa configurar o provider de novo em
Configurações.

Fora do caminho principal: Fase 0/1/2 essencialmente concluídas; Fase 3 (Translation Core) com o
pipeline inteiro pronto; Fase 6 com três providers reais (`OpenAICompatibleProvider`, `DeepLProvider`,
`GoogleGeminiProvider`); Fase 7 (Translation Memory) com exact-match pronto; Fase 9 (Editor) e Fase 11
(Export) com a primeira versão pronta e verificada. Ver `docs/ROADMAP.md` para o detalhamento completo
por fase.

## Completed

- Solution `.sln` (formato clássico — DEC-002) com 7 projetos `src/` + 5 `tests/`, todos compilando
  limpo em x64 com `TreatWarningsAsErrors`.
- Stack fixado e documentado (DEC-001): .NET SDK 10.0.400, Windows App SDK 2.4.0,
  LlamaLogic.Packages 3.8.2, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection
  10.0.11, EF Core/Sqlite 10.0.11.
- `TokenProtector`/`TranslationValidator`/`BatchBuilder`/`TranslationOrchestrator` (Translation) — o
  pipeline de tradução inteiro: protege tokens → chama o provider → reconcilia por ID → restaura →
  valida → grava em translation memory. 84 testes cobrindo essas quatro peças.
- `Sims4PackageService`/`SmartSimStringTableService` (Sims4) — leitura/escrita real de STBL,
  `WriteStringTablesAsync` escrevendo múltiplas STBLs num único package de saída (necessário para
  exportação real de projetos com mais de uma STBL de origem), locale confirmado por teste real, não
  por suposição (DEC-006). 13 testes com packages sintéticos.
- `SqliteTranslationMemory`, `EfProjectService`, `EfProjectStringService`, `EfProviderConfigurationService`
  (Persistence) — SQLite via EF Core, 4 migrations reais (`InitialCreate`, `AddProject`,
  `AddProjectString`, `AddProviderConfiguration`). 28 testes de integração contra SQLite real; um
  pegou um bug real do provider SQLite do EF Core (`ORDER BY` em `DateTimeOffset`, DEC-008).
- `DpapiSecretStore`/`ISecretStore` (Infrastructure) — API key de provider criptografada via DPAPI do
  Windows, nunca em texto puro nem no SQLite (seção 19). 6 testes com criptografia real, em diretório
  isolado.
- `OpenAICompatibleProvider`, `DeepLProvider`, `GoogleGeminiProvider` (Providers) — três providers
  reais, construídos por `ConfiguredProviderFactory` a partir de qual `ProviderKind` o usuário escolheu
  em Configurações (ComboBox de tipo, campo "Modelo" escondido quando não se aplica — DeepL não tem
  esse conceito). `OpenAICompatibleProvider` serve LM Studio/NVIDIA NIM/qualquer endpoint compatível;
  `DeepLProvider` reconcilia resultado por posição no array (não tem id como OpenAI-compatible/Gemini);
  `GoogleGeminiProvider` usa a mesma estratégia de prompt+recuperação de JSON do OpenAI-compatible.
  36 testes contra `HttpMessageHandler` falso — **nenhum dos três nunca foi exercitado contra um
  servidor real além do OpenAI-compatible (ver "Blocked" abaixo)**. Ver DEC-019.
- `HomePage`, `EditorPage`, `SettingsPage` (App) — as três telas de produto reais. Fluxo completo
  (importar → extrair → traduzir/exportar) confirmado rodando o app instalado como MSIX, com um
  `.package` sintético: 4 strings extraídas e persistidas, editor mostrando todas com status,
  "Traduzir pendentes" mostrando a mensagem correta sem provider configurado, "Exportar" gerando e
  validando (round-trip real) um package traduzido de verdade. Ver DEC-011.
- Navegação Home → Editor com parâmetro (`Frame.Navigate(typeof(EditorPage), project.Id)`), resolvida
  no `OnNavigatedTo` da página — primeira navegação com parâmetro do app.
- Edição manual persistente no Editor: coluna de tradução editável, salva em `LostFocus`, cada
  sobrescrita grava a versão anterior em `ProjectStringRevisionRecord` (histórico, mais recente
  primeiro). Filtro por status e busca de texto na lista de strings. Ver DEC-012.
- Drag-and-drop de `.package` na `HomePage` (além do file picker), unificado com a lógica de import por
  `HomePageViewModel.RunImportAsync`. Reimportar um mod já processado agora reporta quantas strings são
  novas/alteradas/reaproveitadas/removidas (`ImportDiffResult`), e uma tradução existente cujo texto de
  origem mudou é marcada `NeedsReview` em vez de descartada. Ver DEC-012.
- Glossário (`IGlossaryService`/`EfGlossaryService`) integrado ao pipeline de tradução: termos globais
  ou por projeto, `NeverTranslate`, filtrados por batch (só os relevantes ao texto daquele lote são
  enviados ao provider) e incluídos no prompt do `OpenAICompatibleProvider`. Ver DEC-012.
- `GlossaryPage`/`GlossaryPageViewModel` (App) — tela para adicionar/listar/remover termos globais do
  glossário, nova entrada "Glossário" na navegação. Verificada rodando o app de verdade via UI
  Automation (adicionar → persiste após reload → remover → persiste a remoção após reload); um bug
  real de binding no botão "Remover" foi encontrado e corrigido nesse processo. Termos por projeto
  ainda não têm UI (só a global). Ver DEC-013.
- Preset "NVIDIA NIM" em Configurações — preenche endpoint/model conhecidos com um clique, sem afirmar
  que a tradução com esse modelo já foi validada (ainda não foi, ver Fase 5 do ROADMAP). Verificado
  rodando o app de verdade. Ver DEC-014.
- Translation Profiles (seção 22, escopo: instruções livres por projeto, não biblioteca de presets
  nomeados) — `TranslationProject.CustomInstructions`, editável na Editor, propagado ao
  `TranslationOrchestrator`/provider igual ao glossário. Corrigido no processo um gap real: o botão
  "Traduzir pendentes" nunca buscava os termos do glossário para repassar ao orquestrador, apesar de
  toda a integração de glossário (DEC-012) estar pronta e testada em todas as camadas — agora
  `EditorPageViewModel.TranslateAsync` chama `IGlossaryService.GetTermsAsync` antes de traduzir. Ver
  DEC-015.
- Fuzzy matching em Translation Memory (seção 26/63) — `StringSimilarity` (Levenshtein) +
  `ITranslationMemory.FindSimilarAsync`; o orquestrador reaproveita uma entrada ≥97% similar em vez de
  chamar o provider, sempre marcando o resultado para revisão (`FuzzyMatchReused`). Ver DEC-016.
- Correção de robustez do provider (`"translation": null` não derruba mais o batch inteiro — DEC-017).
- Barra de progresso real + cancelamento seguro ao traduzir — `EditorPageViewModel.TranslateAsync` agora
  processa e persiste em blocos de 100 strings (não tudo de uma vez), com `ProgressBar`/contagem
  ao vivo e um botão "Cancelar tradução" que preserva o que já foi traduzido. Verificado rodando o app
  de verdade com um servidor HTTP local fake e 500 strings sintéticas. Ver DEC-018.
- `AppDataPaths` (Infrastructure) resolvendo `%LOCALAPPDATA%\TS4Translator\{Data,Logs,Cache,Models}`.
- Composition root em `App.xaml.cs` com todos os serviços registrados (`AddSims4Services`,
  `AddTranslationServices`, `AddPersistenceServices`, `AddInfrastructureServices`,
  `AddProviderServices`), migration aplicada no `OnLaunched`.
- Shell WinUI (NavigationView, Mica, título estendido) a partir do template `winui-navview`.
- Toda decisão relevante registrada em `docs/DECISIONS.md` (DEC-001 a DEC-022).

## In progress

Avaliação linguística humana do corpus real autorizado. Desempenho, completude e integridade estrutural
já foram medidos em `docs/LOCAL_MODELS.md`/DEC-024.

## Blocked

Nada bloqueado por motivo interno ao app. Três itens têm **incerteza de fato pendente de verificação
externa** (não são bloqueios de código, são coisas que só um teste com infraestrutura real resolve):

1. Confirmação humana da qualidade linguística. O corpus autorizado do WickedWhims já foi testado com
   Qwen e Gemini; falta referência pt-BR para medir terminologia e naturalidade em escala.
2. `DeepLProvider` nunca rodou contra a API real — só testado com HTTP falso (DEC-019). Precisa de uma
   API key para validar de fato. `GoogleGeminiProvider` **já foi validado contra a API real** com a
   chave do usuário (DEC-021, depois de corrigir o model id padrão que estava obsoleto) — "Testar
   conexão" retornou "Conexão OK." de verdade.
3. Formato exato do prompt esperado por `nvidia/riva-translate-4b-instruct-v2` — duas fontes
   discordam (playground ao vivo vs. NGC catalog). Precisa de uma API key NVIDIA real para testar.
4. Argos funciona sem Python global por download sob demanda e já passou no benchmark real. Falta a
   certificação final do fluxo dentro do MSIX instalado em um Windows limpo — ver `docs/ARGOS.md`.

## Entregue em 2026-08-31 (DEC-033) — ida e volta por arquivo de texto para um chat de IA

Exportar as strings selecionadas como `.txt`, colar num chat (ChatGPT, Claude, Gemini, Grok), e reimportar
a resposta como tradução. Formato `TS4TRANSLATOR-BUNDLE v1`: um bloco de três linhas por string
(`#id` / `EN:` / `PT:`), escapes `
` para manter cada campo numa linha só, instruções para a IA dentro
do próprio arquivo, e marcadores de início/fim.

A leitura tolera code fence, prosa em volta e rótulos em negrito, e recupera a associação por id, pelo
texto original (quando a IA apaga os ids) ou pela ordem (só se as quantidades baterem, sempre com aviso).
Se nada disso servir, recusa em vez de gravar. Tradução sem id ou com marcador perdido entra como
"Revisar". Seleção por checkbox; sem marcação, exporta o que o filtro mostra.

## Entregue em 2026-08-30, quarta rodada (DEC-030) — roteamento em três faixas, escopo por projeto e UI

- **Roteamento por histórico**: uma string que já voltou marcada para revisão sobe de faixa sozinha,
  o que resolve o caso "Shallow → Rasgo" que nenhuma pontuação por forma enxerga.
- **Até três motores em paralelo** (`IMultiEngineTranslator`), com faixas configuráveis por limiar.
- **Pipeline por projeto**: cada projeto pode ter seus próprios motores ou herdar o global.
- **Estimativa antes de traduzir**: strings, caracteres e requisições por motor.
- **Editor performático**: busca com debounce e teto de 500 linhas renderizadas (o projeto de 149 mil
  travava a janela a cada tecla).
- **Catálogo de modelos** vindo da própria API (OpenAI, compatíveis, Gemini, Anthropic) e **cota** da
  DeepL. Gemini/OpenAI/Anthropic não expõem cota a uma API key comum.
- **UI**: barra lateral compacta, padding reduzido, Configurações em cards, Home com progresso real.

## Entregue em 2026-08-30, terceira rodada (DEC-029) — motores separados por perfil

- **Perfis nomeados**: cada motor (Argos, Google Tradutor, LLM local, OpenAI, Anthropic, Gemini, DeepL) é
  um perfil salvo com endpoint, modelo e chave próprios. Vários do mesmo tipo podem coexistir.
- **Papéis por etapa**: tradução principal, refino linha a linha e revisão em lote podem usar motores
  diferentes. Papel vazio herda o anterior.
- **Dois motores em paralelo**: o lote é dividido por `StringDifficulty` e as duas faixas rodam ao mesmo
  tempo. No WickedWhims real, o limiar padrão 35 manda 79,3% para o motor barato e 20,7% para o bom.
- **Providers novos**: Anthropic (Messages API) e OpenAI como tipo próprio.
- **Configurações reorganizada** em três blocos: motores, quem faz o quê, dois motores.

## Entregue em 2026-08-30, segunda rodada (DEC-028)

- **Modo de revisão por IA**: seletor "Refinar traduções com" no Editor, botão de refino por linha
  (ícone e menu de contexto), e botão "Refinar as que precisam de revisão" para o lote inteiro. O motor
  de refino é independente do que fez a tradução em massa.
- **Cópia do texto original**: a coluna original agora é selecionável, com botão de copiar e menu de
  contexto ("Copiar texto original" / "Copiar tradução"). Antes era um `TextBlock` puro, impossível de
  copiar para colar num tradutor online.
- **Exportação por mod**: um `.package` por arquivo importado, com seletor de pasta quando há mais de um.
  Importar 12 mods gerava um único arquivo, o que parecia que só o primeiro tinha sido traduzido.
- **Provider Google Tradutor** (`ProviderKind.GoogleTranslate`): grátis, sem API key, endpoint ou modelo.
  Ele simplesmente não existia — o que havia era o Google Gemini, que é API paga de LLM.
- Dois bugs de UX achados rodando o app: `LostFocus` marcava a string como editada manualmente sem
  nenhuma alteração; refino idêntico era reportado como falha. Ver DEC-028.

## Corrigido em 2026-08-30 (DEC-027) — três bugs reais encontrados no teste do usuário com o WickedWhims

- **Exportação quebrada** (`ArgumentException: An item with the same key has already been added`):
  as 110 STBLs do mod colapsavam nas mesmas 5 chaves pt-BR e `ToDictionary` lançava em vez de mesclar.
  Agora `StringTableExportPlan` (Core) filtra pelo locale de origem e mescla. Verificado contra o banco
  real do usuário: 110 tabelas → 5, 149.138 strings → 6.779, package escrito e round-trip válido.
- **Importação ingeria todos os idiomas do mod**: 149.138 strings em vez de 6.779 (22 cópias do mesmo
  mod, uma por locale embarcado). Agora só as STBLs do locale de origem são importadas.
- **Reimportar criava um projeto novo em vez de reusar o existente**: `RunImportAsync` sempre chamava
  `CreateAsync`, então a feature de mod-update da seção 25 nunca rodou de verdade no app — sempre
  reportava 100% "nova" e órfanava as traduções anteriores. Agora `FindBySourceFilesAsync` reencontra
  o projeto e `RemoveStringsOutsideAsync` descarta tabelas que saíram da importação.

**Um projeto importado antes desta correção se conserta reimportando o mesmo `.package`** — as
traduções válidas são preservadas e as linhas dos outros idiomas são descartadas.

## Known issues

- `DeepLProvider` nunca foi exercitado contra um servidor real, só contra `HttpMessageHandler` falso em
  teste. `OpenAICompatibleProvider` foi validado pelo usuário contra NVIDIA NIM fora desta sessão
  (achou e ajudou a corrigir bugs reais — DEC-017/DEC-018), mas essa configuração foi perdida no
  incidente da DEC-020 e precisa ser refeita para continuar testando. `GoogleGeminiProvider` **já foi
  validado contra a API real** com a chave do usuário (DEC-021) depois de corrigir o model id padrão
  que tinha sido descontinuado pelo Google.
- **Reinstalar o pacote MSIX com `Remove-AppxPackage` antes de `Add-AppxPackage` apaga todos os dados
  do app** (banco SQLite, segredos DPAPI) — já causou perda de dados real de uma configuração de
  provider do usuário (DEC-020). Procedimento corrigido em `docs/BUILD.md`: incrementar a versão em
  `Package.appxmanifest` e usar só `Add-AppxPackage` (upgrade in-place, sem remover) para qualquer
  redeploy depois da instalação inicial.
- CA1707 (underscore em identificadores) suprimido em `tests/` (DEC-003) — intencional.
- CA1861 suprimido em todo o projeto `TS4Translator.Persistence` (DEC-007) por causa do código gerado
  pelo `dotnet ef migrations add` — intencional.
- Regex de tags de markup do `TokenProtector` (`<...>`) é genérico o suficiente para, em teoria,
  capturar um "menor que" seguido de "maior que" que não seja markup real — nunca testado contra STBL
  real do jogo (só contra exemplos sintéticos da própria suíte de testes e um `.package` sintético de
  verificação manual). Repriorizar se aparecer em mods reais.

## Tests currently failing

Nenhum. A suíte tem 376 testes e todos passam nesta máquina (incluindo os quatro DPAPI, que só falham
em sandbox sem perfil Windows carregado).
`TS4Translator.Core.Tests` finalmente tem testes reais (`StringSimilarityTests`) — estava vazio (só
scaffolding do template) desde o início do projeto.
`TS4Translator.App` não tem testes automatizados de UI (não há framework de UI test configurado), mas
o fluxo completo (importar → extrair → editor → exportar com round-trip) foi verificado manualmente
com sucesso, rodando o app de verdade — ver DEC-011. As features desta rodada (edição manual/histórico,
drag-and-drop, diff de reimportação, glossário — DEC-012) foram cobertas por teste automatizado; a
tela de Glossário (DEC-013) também foi verificada rodando o app de verdade. As demais telas novas
(edição manual no Editor, drag-and-drop na Home) **não** foram re-verificadas rodando o app desta vez,
por instrução explícita do usuário: ele vai avaliar e testar tudo pessoalmente no ambiente real depois
desta conclusão, e qualquer problema encontrado entra numa rodada de correção separada.

## Commands to build

Ver `docs/BUILD.md` — inclui build/test normais, como rodar via `dotnet run` para desenvolvimento do
dia a dia, e o procedimento completo (empacotar MSIX, assinar, instalar) necessário para testar
qualquer fluxo que use `FileOpenPicker`/`FileSavePicker` ou outra API mediada por broker do Windows.

## How to run the application

Ver `docs/BUILD.md`. Resumo: `dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64`
para desenvolvimento normal; empacotar como MSIX e instalar para testar picker de arquivo/salvar.

## Next milestones

Ver "Próximas tarefas concretas" no final de `docs/ROADMAP.md`. Em ordem:
1. Usuário reconfigura o provider NVIDIA NIM (config perdida, DEC-020) e retoma o teste de tradução
   real. `GoogleGeminiProvider` já foi validado contra a API real (DEC-021).
2. Testar `DeepLProvider` contra a API real dele.
3. UI de glossário por projeto (hoje só o escopo global tem tela — DEC-013).
4. UI de "sugestões de fuzzy match" navegável no Editor (a API já existe com limiar mais permissivo,
   0.85 — DEC-016 — só falta a tela).
5. `TranslationJob`/progresso para lotes grandes, `SettingsPage` em abas reais (seção 41).

## Limitação conhecida do provider Google Tradutor (medida ao vivo, 2026-08-30)

O endpoint público `translate_a/single` é grátis e não pede API key, mas **estrangula por IP de forma
agressiva**. Medições reais desta máquina:

- Sem cabeçalho `User-Agent`: **429 na primeira requisição**, sempre. Com qualquer `User-Agent` não
  vazio: 200 OK. O provider envia `TS4Translator/1.0`; remover esse cabeçalho quebra o provider por
  completo (há teste de regressão).
- Seis requisições em paralelo: quatro voltaram 429.
- Depois de algumas dezenas de requisições de teste, o IP ficou recusando **até requisições sequenciais**
  por vários minutos.

Por isso o provider é sequencial, com 250 ms entre requisições, e uma falha por item não derruba o lote
(o item fica sem resultado e continua pendente). Se **todas** falharem, a exceção diz explicitamente que é
limite por IP.

**Uso realista:** ótimo como motor de **refino** (uma string por vez, ou algumas dezenas marcadas para
revisão) e para mods pequenos. Para traduzir 6.779 strings de uma vez, não — use Argos (offline, sem
limite) ou uma API paga. Uma verificação ao vivo do lote completo de 6 strings ficou pendente porque os
testes desta sessão esgotaram a cota do IP; o caminho unitário está coberto por teste.

## Divisão por dificuldade no corpus real (medida em 2026-08-30)

`TURBODRIVER_WickedWhims_Tuning.package`, 6.779 strings inglesas. Pontuação: mediana 9, p75 27, p90 50,
máximo 94.

| Limiar | Fáceis (motor barato) | Difíceis (motor bom) | % difíceis |
|-------:|----------------------:|---------------------:|-----------:|
| 20 | 4.468 | 2.311 | 34,1% |
| 25 | 4.995 | 1.784 | 26,3% |
| 30 | 5.167 | 1.612 | 23,8% |
| **35 (padrão)** | **5.379** | **1.400** | **20,7%** |
| 40 | 5.629 | 1.150 | 17,0% |
| 50 | 6.037 | 742 | 10,9% |
| 60 | 6.418 | 361 | 5,3% |

Serve para dimensionar custo: no limiar padrão, uma API paga é acionada para cerca de um quinto do mod.

## Rodada de 2026-08-31 — diagnóstico do ida-e-volta com chat, memória e glossário

Partiu de um caso real: mil strings do `[Kritical]DreamsOfSurrender_1_35` foram para um chat, voltaram
e o app reportou avisos suficientes para o usuário achar que o arquivo tinha vindo quebrado. **Não tinha.**
A análise do arquivo e do banco mostrou 1000/1000 casadas por id, zero marcador perdido, `\n` e `\`
preservados em 100% dos registros, e 1000 linhas gravadas como `ManuallyEdited` com `IssueSummary` vazio.
O problema era o app contando a história errada. Ver DEC-034 e DEC-035.

O que mudou, com os números medidos no corpus real:

| Item | Antes | Depois |
|---|---|---|
| Avisos numa importação limpa de 1000 strings | 134 (128 falsos) | **1 aviso** — a única falha real |
| Ver a lista completa de problemas | impossível (3 + "(+184)") | painel agrupado + relatório em `.txt` |
| Alcance máximo de uma exportação | 500 (o que a lista renderiza) | o mod inteiro, em partes numeradas |
| Identificadores de tuning mandados ao tradutor | 866 de 4.133 (21%) | 0 por padrão |
| Traduções manuais/do chat na memória | 2 de 1.275 | todas as que passam pela política |
| Glossário chegando ao chat | não ia | vai, e a volta é conferida |
| Termos no glossário | 0 | editáveis, por projeto ou globais, criáveis do editor |

Estado da memória no banco do usuário nesta data: 8.427 entradas (8.330 `argos-en-pb-1.9`,
97 `google-gemini`), todas en-US→pt-BR. O backfill da tela de Memória traz mais 1.208 traduções feitas
à mão que estavam trancadas dentro dos projetos.

**Ainda não feito:** o filtro de `StringTranslatability` vale para a exportação ao chat, mas não para
"Traduzir pendentes" — os mesmos identificadores continuam indo para os motores pagos numa rodada em
massa. É a mesma economia (21% do mod), num caminho diferente.
