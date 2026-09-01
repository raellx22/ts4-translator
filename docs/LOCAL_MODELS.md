# Modelos locais — baseline de 8 GB de VRAM

Última medição: 2026-08-29, Windows, RTX 4060 Ti 8 GB, Bionic/LM Studio 1.1.0,
runtime llama.cpp CUDA 12, `Qwen3.5 4B Q4_K_M`.

## Escolha atual

O candidato local de melhor desempenho para tradução é agora **Hy-MT2 1.8B Q8_0**. O projeto Linux
anterior já o carregava como `hy-mt2-1.8b`, com contexto 8.192 e oito previsões simultâneas. Repetir
essa configuração no mesmo hardware, agora no Windows/Bionic, reduziu a projeção do WickedWhims de
67 para 18,5 minutos no contrato item a item e para aproximadamente 14 minutos no protótipo de lotes
pequenos com fallback. Isso prova que a maior parte do gap era modelo + contrato, não Linux versus
Windows.

O **Qwen3.5 4B Q4_K_M** permanece como fallback generalista para providers OpenAI-compatible. O
Hy-MT2 requer um perfil próprio: lotes de 8/1.200, oito filas, limite de 500 caracteres por entrada,
deduplicação e retry individual para lote incompleto. Aplicar a configuração genérica 32/3.500 ao
Hy-MT2 excedeu o contexto real no LM Studio.

Fontes: [Qwen3.5 4B model card](https://huggingface.co/Qwen/Qwen3.5-4B),
[EraX Translator V1.0 model card](https://huggingface.co/erax-ai/EraX-Translator-V1.0),
[Hy-MT2 1.8B GGUF oficial](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF).

Argos Translate continua útil como fallback totalmente offline e determinístico, especialmente em
CPU, mas não substitui o modelo local principal para tom, contexto, glossário e textos longos. O gap
de empacotamento Python/CTranslate2 no MSIX permanece documentado em `docs/ARGOS.md`.

## Configuração validada no Bionic

- Automatic Optimize Based on Hardware: desligado.
- Context Length: 8.192.
- GPU Offload: 31/31 camadas.
- Flash Attention: ligado.
- Max Concurrent Predictions: 2.
- Evaluation Batch Size: 2.048; Physical Batch Size: 512.
- API local: `http://127.0.0.1:1234/v1`.
- Model id da instância: `qwen3.5-4b`.

Com uma fila, o uso total reportado pela GPU caiu de quase 8 GB (AutoFit escolheu 75.776 tokens) para
cerca de 5,3 GB. Com duas filas e 4.096 tokens, o pico medido ficou em aproximadamente 5,4 GB. O
contexto de 8.192 foi necessário quando o primeiro lote real do WickedWhims chegou a 4.482 tokens só
de entrada; ele carregou e concluiu o teste na RTX 4060 Ti 8 GB sem OOM.

**Não aumentar contexto como tentativa de melhorar qualidade.** Contexto é capacidade, não
inteligência: ele aumenta o KV cache e reduz a VRAM disponível. O corpus real provou que 4.096 é
insuficiente para este app; 8.192 é o baseline. A documentação do Ollama explica que o requisito de memória do
paralelismo cresce aproximadamente como `parallel × context`, e o LM Studio usa continuous batching:
[Ollama FAQ](https://docs.ollama.com/faq),
[LM Studio parallel requests](https://lmstudio.ai/docs/app/advanced/parallel-requests).

## Resultado medido

| Cenário | Itens | Tempo de parede | Tokens úteis/s | Resultado |
|---|---:|---:|---:|---|
| 1 lote pequeno | 8 | 3,01 s | ~40 | 8/8 IDs, 0 reasoning tokens |
| 1 lote | 32 | 11,26 s | 42,1 | 32/32 IDs, 0 reasoning tokens |
| 1 lote cheio | 100 | 34,90 s | 44,4 | 100/100 IDs, 0 reasoning tokens |
| 2 lotes simultâneos | 64 (2×32) | 15,56 s | 69,4 agregados | 64/64 IDs, 0 reasoning tokens |
| WickedWhims real, local | 200 | 118,60 s | — | 196 aprovadas; 4 para revisão após auditoria |
| WickedWhims real, Gemini (32/lote) | 200 | 9,50 s | — | 199 aprovadas; 1 revisão |
| WickedWhims real, Gemini (80/lote) | 200 | 40,91 s* | — | 198 aprovadas; 2 para revisão após auditoria |
| Hy-MT2 Q8, item a item, 8 filas | 200 | 34,24 s | ~159 saída | 195 aprovadas; 5 revisões; projeção 18,5 min |
| Hy-MT2 Q4, item a item, 8 filas | 200 | 31,19 s | ~155 saída | 196 aprovadas; 4 revisões; projeção 16,9 min |
| Hy-MT2 Q8, lote 8/1.200 + fallback | 200 | 25,85 s | ~240 saída | 192 aprovadas; 8 revisões; projeção 14,0 min |

\* Inclui espera real por quota acumulada. A execução de 32/lote mostrou a latência sem rate limit;
a de 80/lote valida principalmente integridade e redução de chamadas.

Duas filas terminaram os mesmos dois lotes cerca de 31% mais rápido que executá-los sequencialmente
(15,56 s contra aproximadamente 22,52 s). O ganho não é 2× porque as duas sequências compartilham a
mesma GPU. Mais de duas filas não é o padrão: precisa ser medido por GPU e multiplica o cache.

O benchmark reproduzível está em `tools/Benchmark-LocalModel.ps1`, por exemplo:

```powershell
./tools/Benchmark-LocalModel.ps1 -BatchSize 32 -ConcurrentRequests 2
```

## O gap encontrado

1. O Qwen3.5 pensa por padrão. No endpoint OpenAI-compatible do LM Studio, um lote de 8 strings gastou
   os 512 tokens de saída inteiramente em raciocínio e retornou conteúdo vazio. O payload local agora
   envia `reasoning_effort: "none"`; a mesma requisição passou a responder em ~3 s.
2. O contrato de saída dependia apenas do prompt e não limitava tokens. Endpoints locais agora recebem
   JSON Schema estrito, todos os IDs obrigatórios e um `max_tokens` calculado. Ver
   [LM Studio structured output](https://beta.lmstudio.ai/docs/developer/openai-compat/structured-output).
3. A translation memory abria o SQLite duas vezes por string e carregava todos os candidatos para cada
   fuzzy match — crescimento efetivamente quadrático. A busca agora é uma leitura em lote por
   checkpoint, com filtro de comprimento antes de Levenshtein, e a gravação é transacional em lote.
4. O app traduzia e persistia sequencialmente. Providers locais agora recomendam duas filas; o
   orquestrador executa até dois batches em paralelo e faz uma única gravação de memória e uma única
   gravação das strings do projeto por checkpoint.
5. Um batch global de 100/8.000 não serve para todos os providers. No Qwen ele excedeu contexto e
   perdeu placeholder em string longa; o provider local agora recomenda 32 itens/3.500 caracteres.
   O Gemini recomenda 80/6.400, e o orquestrador respeita limites por provider.
6. O schema do Gemini não aceita `additionalProperties`; o app agora usa apenas `properties` e
   `required`, mantendo todos os IDs fixos sem enviar o campo incompatível.
7. O free tier observado do `gemini-3.5-flash-lite` limita 15 requests/min. O retry anterior ignorava
   `google.rpc.RetryInfo` e repetia cedo demais. `HttpRetry` agora obedece tanto `Retry-After` quanto o
   `retryDelay` do corpo.
8. Alguns mods já trazem markup desequilibrado na origem. O validador não reprova mais uma tradução
   que preservou exatamente esse defeito; perda/duplicação de tags continua crítica via TokenProtector.
9. `IdenticalToSource` era apenas warning e, por isso, uma frase inglesa longa entrava como sucesso e
   seria gravada na memory. Todo warning agora vira `NeedsReview`; tokens puros e flags `# ...` são
   isentos, evitando ruído sem aprovar texto natural não traduzido.
10. O projeto Linux não usava o Qwen: usava Hy-MT2 1.8B, uma string por request, oito requests em
    paralelo, deduplicação e descarte de textos acima de 500 caracteres. Reproduzir isso no Windows
    trouxe ganho de aproximadamente 72% sobre o Qwen, eliminando o sistema operacional como causa
    principal.
11. Q4 não acelerou a geração por token: o pequeno ganho de tempo veio de respostas mais curtas e
    veio acompanhado de piora linguística observável. Q8 é a variante recomendada nesta GPU.
12. Lotes pequenos podem reduzir a estimativa para ~14 minutos, mas a saída estruturada foi instável:
    uma execução perdeu oito IDs e outra exigiu fallback individual. O provider especializado deve
    reexecutar somente IDs ausentes, com tokens perdidos ou texto natural idêntico.

## Projeção do WickedWhims real

O package autorizado em `X:` contém 110 STBLs, cinco em inglês, **6.779 strings** e 440.402
caracteres (mediana 35, P90 157, máximo 2.159). A estimativa inicial de 25 mil strings não se confirmou.

- Qwen local: 118,60 s/200; projeção linear **67 min**, custo de API zero. Não vale executar o
  mod inteiro quando a meta é 5–6 minutos.
- Hy-MT2 Q8 local: 34,24 s/200 no pipeline Linux equivalente (**18,5 min**) ou 25,85 s/200 em lotes
  pequenos com fallback (**14,0 min**). É uma melhora grande, mas ainda não atinge 5–6 minutos numa
  RTX 4060 Ti 8 GB. O limite restante é geração de saída, não contexto.
- Gemini 3.5 Flash-Lite: lote de 80 preservou estrutura em 200/200 e deixou 2 textos naturais para
  revisão; o mod exige aproximadamente 85 requests. No free
  tier observado, o piso matemático é **5 min 40 s**, com expectativa prática de cerca de 6–7 min.
- Tokens projetados: ~311.529 de entrada e ~309.563 de saída. Pelo preço standard medido em
  2026-08-29, custo equivalente aproximado de **US$ 0,87**; no free tier elegível, custo cobrado zero.

## Regras que ficam no app

O modelo recebe somente `id` + texto já protegido. O app continua responsável por proteger/restaurar
placeholders, selecionar termos relevantes do glossário, exigir cada ID uma vez, validar o resultado,
marcar falhas para revisão, manter revisões e salvar checkpoints. Texto de mod é dado não confiável:
instruções encontradas dentro dele devem ser traduzidas, nunca executadas ou respondidas.

O benchmark já usa STBLs reais autorizadas e valida estrutura, placeholders e completude. Ainda falta
uma referência humana pt-BR para pontuar terminologia, consistência e naturalidade; aprovação automática
não equivale a revisão linguística profissional.

## Rodada nativa (in-process, llama.cpp via LLamaSharp) — 2026-08-31

Mesma GPU (RTX 4060 Ti 8 GB), mesmo modelo (`Hy-MT2-1.8B-Q8_0.gguf`, o arquivo que já estava na pasta do
LM Studio), mesmos 200 strings reais do WickedWhims. A diferença é o runtime: **sem LM Studio, sem HTTP**
— llama.cpp carregado dentro do processo .NET. Protótipo em `tools/TS4Translator.LocalRuntimeProbe`.

| Configuração | Tempo | tok/s saída | Aprovadas | Prefill |
|---|---:|---:|---:|---:|
| A · 1 sequência, sem cache de prefixo | 64,82 s | 80 | 176/200 | 31.592 |
| B · 8 sequências, sem cache de prefixo | 16,69 s | 309 | 177/200 | 31.592 |
| **C · 8 sequências, com cache de prefixo** | **13,92 s** | **371** | 177/200 | **4.130** |
| *(baseline documentado: LM Studio, item a item, 8 filas)* | *34,24 s* | — | *195/200* | — |

**As duas comparações internas, sem confundidores** (A, B e C são o mesmo prompt, mesmo sampler, mesma
build — só muda o que está sendo testado):

- **Paralelismo: 3,9×** (64,82 → 16,69 s). O `BatchedExecutor` decodifica todas as sequências vivas num
  passo só; é daí que vem o ganho.
- **Cache de prefixo: 1,20×** (16,69 → 13,92 s), com **87% menos prefill** (31.592 → 4.130 tokens). O
  prompt de sistema é processado uma vez para a corrida inteira em vez de uma vez por string. É
  exatamente o que um endpoint HTTP não expõe.

**Determinismo: 200/200 respostas idênticas** entre duas execuções da config C (greedy, `temperature = 0`).
Isso o LM Studio não garantia.

**Marcadores inventados: 0** em todas as configurações — contra o incidente do Gemini 3.1 Flash-Lite
(DEC-036), que inventou 29.

### Onde a comparação com o LM Studio é justa e onde não é

O tempo é comparável no essencial (mesmo modelo, hardware, quantização e corpus) mas tem dois
confundidores a favor do nativo: o protótipo usa um prompt de sistema mais curto e devolve **texto puro
em vez do envelope JSON**, então gera menos tokens por item. Parte dos 2,46× vem disso, não do runtime.
Os 3,9× do paralelismo e os 1,20× do cache são medidos contra si mesmos e não têm esse problema.

**As 177/200 não medem qualidade do modelo, medem o prompt do protótipo.** Das 23 reprovações:

- **14 `MissingToken`**: o modelo juntou "Check to enable…\nUncheck to disable…" numa frase só e perdeu a
  quebra de linha protegida. Uma instrução, não uma limitação — o contrato real do app é mais explícito.
- **14 `IdenticalToSource`**: rótulos como "Desire Switch" que voltaram iguais. Pelo `StringTranslatability`
  atual isso é `Term`, ou seja, observação e não aviso.
- **1 `UnbalancedMarkup`** em `<i><b>naughty</b></i>`.

### Projeção

13,92 s / 200 strings → **~7,9 minutos** para os 6.779 do WickedWhims, contra 18,5 min (LM Studio item a
item) e 14,0 min (lotes pequenos) documentados acima. Com o contrato JSON completo do app o número sobe;
a estimativa honesta é **8 a 11 minutos**.

### O custo real de empacotamento, medido

O pacote `LLamaSharp.Backend.Cuda12.Windows` **não traz o runtime CUDA**. `ggml-cuda.dll` importa
`cudart64_12.dll`, `cublas64_12.dll` e `nvcuda.dll`; só o último vem com o driver. Numa máquina sem CUDA
Toolkit, nada carrega — e a detecção do LLamaSharp cai silenciosamente para CPU, que foi o primeiro
resultado errado desta investigação.

| Componente | Tamanho |
|---|---:|
| `ggml-cuda.dll` (LLamaSharp) | 526 MB |
| `cublasLt64_12.dll` | 643 MB |
| `cublas64_12.dll` | 108 MB |
| `cudart64_12.dll` | 0,5 MB |
| Modelo `Hy-MT2-1.8B-Q8_0.gguf` | 1,8 GB |
| **Total do caminho GPU** | **~3,1 GB** |

Tem que ser download sob demanda, como o Argos já faz — nunca dentro do MSIX. E o `WithLibrary()` deve
apontar o caminho explicitamente: a detecção automática do LLamaSharp procura um Toolkit que o usuário
final não vai ter.

### Como reproduzir

`tools/TS4Translator.LocalRuntimeProbe` virou o smoke test do provider real (`LocalModelProvider` +
`LocalModelRuntime`, pontuado pelo `TokenProtector` e pelo `TranslationValidator` de produção). O corpus é
derivado do banco local e ignorado pelo git: gere `corpus-200.json` com id/text das strings do projeto
desejado, coloque ao lado do executável e rode:

```powershell
dotnet run -c Release --project tools\TS4Translator.LocalRuntimeProbe
```

## O provider `LocalModel` — medição do caminho real (2026-08-31)

Não é mais protótipo: `ProviderKind.LocalModel` roda pelo mesmo caminho do app (perfil → factory →
orquestrador), com **glossário e instruções do projeto no prompt**, e a pontuação vem do
`TranslationValidator` de produção.

| | Tempo | Aprovadas | Marcadores perdidos |
|---|---:|---:|---:|
| Protótipo (prompt mínimo, sem glossário) | 13,92 s | 177/200 | 14 |
| **Provider real (glossário + instruções + retry)** | **18,21 s** | **184/200** | **7** |
| LM Studio, item a item, 8 filas | 34,24 s | 195/200 | — |

Das 16 que sobram, **12 são `IdenticalToSource`** em rótulos como "Desire Switch", que o
`StringTranslatability` classifica como `Term` — ou seja, observação e não falha. Descontando essas, o
provider fica equivalente ao LM Studio em qualidade, em metade do tempo, com glossário funcionando.

O que fechou o gap de 177 para 184:

1. **Regra de marcador explicando o que ele é.** "Um marcador entre duas frases significa que as frases
   continuam separadas" — a falha dominante era o modelo fundir "Check to enable…\nUncheck to disable…"
   numa frase só.
2. **Retry por item.** Quem perdeu marcador vai de novo, sozinho, com a regra reforçada. Só é aceito se
   a segunda tentativa realmente consertou; senão a primeira resposta fica e o validador marca para
   revisão como sempre. Localmente isso custa segundos.
3. **`ProtectedMarkers.Normalize`.** O modelo às vezes escreve quatro underscores onde o marcador pede
   dois, e a restauração colava um `__` na palavra seguinte — "…reiniciar o jogo.\n\n__Salve e reinicie".
   Nada sinalizava, porque os dois marcadores estavam lá. O recorte tem lookahead porque `\n\n` protege
   como **dois marcadores adjacentes**, que legitimamente têm quatro underscores no meio; a primeira
   versão sem lookahead fundia os dois e quebrava uma tradução correta. O teste pegou.

### Instalação (hoje, manual)

O runtime nativo e o modelo ficam em `%LOCALAPPDATA%\TS4Translator\Models\Local\`:

```
llama\cuda12\   as 8 DLLs (llama, ggml*, cudart64_12, cublas64_12, cublasLt64_12)  ~1,3 GB
models\         qualquer .gguf                                                     ~1,8 GB
```

A tela de Configurações mostra o estado e **avisa em amarelo quando cairia para CPU** — o
`LocalModelRuntime` nunca deixa isso para a autodetecção do LLamaSharp, que procura um CUDA Toolkit que
o usuário final não tem e escolhe a CPU em silêncio.

Falta o download automático desses dois pacotes; hoje é cópia manual para a pasta.

## Por que 28 rótulos curtos voltaram iguais ao original (2026-08-31)

Traduzindo `Skarrd_Perverted_Expansion_Pack_CustomDrinksAddon` (161 strings) com o motor local: 133
traduzidas, **28 devolvidas idênticas**. Todas curtas e aparentemente triviais — `Make Penis Enlarger`,
`Panty Heat Broadcaster`, `insta-drunk recipe description`, `Tickled Balls`.

O que chamava atenção é que strings **mais difíceis** passaram: `From consuming a "Panty Heat" beverage.`
virou `Ao consumir a bebida "Panty Heat".`, e `Penis Enlarger` sozinho virou `Aumentador de Pênis`. Mas
`Make Penis Enlarger` não.

### A causa

Não é limitação de tamanho nem de dificuldade. **O modelo não reconhece o jargão como texto traduzível.**
Sem uma frase em volta, `Panty Heat Broadcaster` parece um identificador — um nome de coisa — e um motor
de tradução, corretamente, não traduz nomes. `Penis Enlarger` tem significado ordinário em inglês e passa;
`Make Penis Enlarger` vira, para o modelo, "o comando Make aplicado a um produto chamado Penis Enlarger",
e ele devolve o rótulo inteiro.

Medido com as mesmas 28 strings, variando só o que o prompt diz:

| Variante | Voltaram iguais |
|---|---:|
| A · como está hoje | 27/28 |
| B · instrução genérica "traduza rótulos curtos mesmo com duas ou três palavras" | 25/28 |
| **D · três termos no glossário** | **5/28** |

Os três termos são o que qualquer usuário digitaria na tela de Glossário:

```
Broadcaster         -> Emissor
Make                -> Fazer
recipe description  -> descrição da receita
```

**Instrução genérica não resolve (27→25). Dizer o que o jargão significa resolve (27→5).**

Um efeito de segunda ordem vale registrar: `Sensuously Soaked` e `Tickled Balls` passaram a ser
traduzidos também, e não têm relação nenhuma com os três termos. O bloco de glossário no prompt sinaliza
que, neste domínio, esse tipo de palavra **é** para traduzir — o modelo sai do modo "isto é um nome
próprio" por inteiro.

### O que isso quer dizer para o app

É exatamente o caso de uso do glossário, e é a diferença estrutural entre este motor e o Argos, que
declara `SupportsGlossary: false` e não teria como ser consertado assim.

As 5 que sobram são palavras inventadas sem significado ordinário (`Instarection`, `Insta-Drunk`) —
deixá-las como estão é defensável.

**Ideia em aberto:** `Broadcaster` é vocabulário padrão de tuning do Sims 4, não uma invenção deste mod.
Um glossário inicial com o jargão comum da plataforma, embutido no app, resolveria isso para todo mundo
sem exigir que cada usuário descubra o problema sozinho. Precisa de curadoria, então fica registrado como
proposta e não como feito.

## Glossário embutido: o que a mineração dos 17 mods realmente mostrou

Proposta anterior: embutir no app um glossário do jargão comum do Sims 4, já que `Broadcaster` parecia
vocabulário de plataforma. `tools/GlossaryMiner` foi escrito para escolher os termos com base nos 169.390
strings já traduzidos, e **o resultado contradisse a proposta**.

### O sinal certo é retenção, não taxa de falha

A primeira métrica creditava a falha de cada string a todas as suas palavras, e devolveu vocabulário
comum — `Sex`, `Bed`, `Bar`, `Strip`. A string falhava por outro motivo e a palavra só estava junto.

A métrica que funciona é **retenção**: com que frequência a palavra inglesa continua, literal, na
tradução. Baixa = o motor traduz e não precisa de glossário. Alta = ou é jargão que ele não sabe
traduzir, ou nome próprio que ele acerta em manter. Com um segundo filtro necessário: uma palavra que
aparece em traduções cujo original **não** a continha é portuguesa também (`normal`, `menu`, `total`,
`bar`), e retenção alta nessas não significa nada.

### O jargão do Sims não é o problema

| Termo | Usos | Mods | Voltaram iguais |
|---|---:|---:|---:|
| Whim | 1.274 | 9 | **0** |
| Trait | 1.433 | 11 | 1 |
| Moodlet | 183 | 4 | 1 |
| Buff | 567 | 12 | 3 |
| Aspiration | 155 | 6 | 1 |
| Interaction | 2.236 | 11 | 22 |
| **Broadcaster** | **27** | **2** | **8** |

Os termos realmente transversais **quase nunca falham** — o motor já os traduz. E `Broadcaster`, que
originou a ideia, aparece em **2 mods e 27 vezes**: não é vocabulário de plataforma, é vocabulário do
Skarrd. Eu tinha generalizado a partir de um caso.

Um glossário embutido com esse jargão gastaria contexto em todo lote para resolver um problema que não
existe.

### O que a mineração aponta como valioso é outra coisa

Nomes próprios que o motor **corrompe** ao traduzir:

| Termo | Usos | Mods | Retenção | Evidência |
|---|---:|---:|---:|---|
| Sims / Sim | 49.120 | 17 | 99% / 96% | 270 linhas voltaram iguais; e `Premade NPC Sims` virou `os símbolos pré-fabricados` |
| WickedWhims | 1.175 | 4 | 64% | 36% das vezes o nome do mod foi traduzido |
| Simoleons | 158 | 5 | 98% | moeda do jogo |
| Sexpertise | 209 | 6 | 99% | perícia do WickedWhims |
| WooHoo | 139 | 4 | 98% | termo do jogo |

Ou seja: o glossário embutido que vale a pena é **pequeno e só de "nunca traduzir"** — meia dúzia de
nomes próprios —, não um dicionário de jargão. Custa quase nada de contexto e evita uma falha concreta e
feia.

O jargão específico de mod (`Broadcaster`, `Insta-Drunk`, `Panty Heat`) pertence ao **glossário por
projeto**, que é exatamente para isso.
