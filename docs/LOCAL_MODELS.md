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
