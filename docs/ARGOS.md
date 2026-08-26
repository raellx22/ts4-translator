# Argos Translate (offline provider) — pesquisa e arquitetura, sem implementação

Este documento existe para ser honesto sobre um item do brief (seção 3/4: Argos Translate como
provider offline/gratuito de primeira classe) que **não foi implementado** nesta fase do projeto, e
para deixar registrado o suficiente de pesquisa e design para que uma sessão futura não recomece do
zero. Ver `docs/ROADMAP.md` Fase 0/4 para o contexto de quando essa pesquisa foi feita.

## Por que não foi implementado

Empacotar um runtime Python completo (Argos Translate depende de `ctranslate2`, `sentencepiece`,
`sacremoses`, `stanza`/`spacy` dependendo da direção de tradução, e os próprios modelos `.argosmodel`,
que somam facilmente algumas centenas de MB por par de idiomas) dentro de um app WinUI 3/MSIX é, em si,
um projeto de integração de sistemas com escopo comparável ao resto deste app — não uma tarde de
trabalho. Diferente de escrever `OpenAICompatibleProvider` (que é só um cliente HTTP), aqui há decisões
reais de empacotamento, distribuição e testes de instalação num ambiente Windows limpo que exigiriam
verificação prática (instalar em uma máquina sem Python, medir o tamanho final do instalador, testar
que o `ctranslate2` nativo carrega corretamente dentro do sandbox de um MSIX) que esta sessão não tem
como fazer de forma confiável. Implementar uma versão que compila mas nunca foi de fato instalada e
testada seria pior do que não ter a feature — exatamente o tipo de "meio-pronto que parece pronto" que
o brief pede para evitar (seção 60).

## O que já é verdade, hoje, sobre a arquitetura

A boa notícia é que **nenhuma mudança arquitetural é necessária** para adicionar Argos depois — o
projeto já foi desenhado para isso:

- `ITranslationProvider` (`Core/Translation/`) é a única superfície que um provider precisa
  implementar. `Argos` seria `TS4Translator.Providers.Argos.ArgosTranslateProvider : ITranslationProvider`,
  igual a `OpenAICompatibleProvider` hoje.
- `ProviderCapabilities.SupportsLocalExecution: true` e `RequiresApiKey: false` já existem no contrato
  exatamente para diferenciar um provider local (Argos) de um provider HTTP (OpenAI-compatible).
- `ITranslationOrchestrator` não sabe nem se importa como um provider traduz um batch — protege
  tokens, chama `TranslateBatchAsync`, reconcilia por `Id`, restaura, valida. Um provider local que
  responde na hora (sem round-trip de rede) funciona sem nenhuma mudança no orquestrador.
- `AppDataPaths.ModelsDirectory` (`Infrastructure/`) já resolve
  `%LOCALAPPDATA%\TS4Translator\Models` — o lugar óbvio para `.argosmodel` baixados ficarem, fora da
  pasta de instalação do MSIX (que é somente-leitura depois de instalado).

## Duas abordagens candidatas (pesquisadas, nenhuma prototipada)

### 1. Sidecar por processo — `argostranslate` via um executável Python separado, comunicação por stdin/stdout ou um socket local

- **Como funcionaria**: o instalador (ou um passo de "baixar Argos" na UI) baixa um Python embarcável
  (`python-3.x-embed-amd64.zip`, distribuição oficial, ~15-25MB) mais as wheels de `argostranslate` e
  suas dependências para uma pasta em `AppDataPaths`; `ArgosTranslateProvider` inicia esse Python como
  processo filho (`Process.Start`) rodando um pequeno script que lê JSON de um batch pela stdin e
  escreve JSON de resultado pela stdout, ficando vivo entre chamadas (evita o custo de start-up do
  interpretador a cada batch).
- **Vantagem**: isola completamente o runtime Python do processo do app — uma falha do lado Python não
  derruba o WinUI; mais fácil de testar/depurar o lado Python isoladamente com um terminal comum.
- **Risco**: gerenciar o ciclo de vida do processo filho corretamente dentro de um pacote MSIX
  (permissões, o processo filho sobrevivendo ao encerramento do app, antivírus/SmartScreen reagindo a
  um `.exe`/`pythonw.exe` desconhecido rodando de dentro da pasta do app) é a parte que precisaria de
  teste real em uma máquina limpa antes de confiar nisso.

### 2. Embutido no processo — `pythonnet` (`ArgosTranslate.PyNet`, mencionado na pesquisa original) hospedando o interpretador Python dentro do próprio processo .NET

- **Como funcionaria**: `pythonnet` inicializa um runtime Python embutido no AppDomain do .NET;
  chamadas a `argostranslate.translate.translate(text, from_code, to_code)` acontecem via interop
  direto, sem serialização JSON nem IPC.
- **Vantagem**: sem overhead de IPC, sem gerenciar processo filho separado, latência menor por chamada.
- **Risco**: acopla o ciclo de vida do interpretador Python ao processo WinUI (uma trava/crash do lado
  Python pode derrubar o app inteiro); `pythonnet` dentro de um pacote MSIX sandboxed é uma combinação
  bem menos testada na comunidade do que fora de um pacote — maior risco de superfície desconhecida.

**Nenhuma das duas foi prototipada.** A pesquisa original (arquivada nesta sessão, resumida acima)
achou que a abordagem 1 (sidecar) é a mais segura para um primeiro protótipo — isola a superfície de
falha — mas isso não foi validado com código real.

## O que uma sessão futura deveria fazer antes de escrever `ArgosTranslateProvider`

1. Prototipar a abordagem 1 (sidecar) num diretório isolado, fora do MSIX, e confirmar: o Python
   embarcável consegue rodar `argostranslate` sem exigir uma instalação de Visual C++ Redistributable
   adicional (algumas wheels nativas de `ctranslate2` podem exigir isso); o tamanho total (runtime +
   pelo menos um par de modelo en↔pt) é aceitável para distribuir.
2. Só depois disso, integrar dentro do MSIX e testar num Windows limpo (sem Python, sem
   Redistributables extras) — exatamente o tipo de verificação que DEC-010 já mostrou ser
   imprescindível para qualquer coisa que dependa de empacotamento.
3. Implementar `ArgosTranslateProvider` só depois dos dois passos acima terem funcionado de verdade —
   não antes.

## Não fazer

Não implementar um `ArgosTranslateProvider` que apenas chama um `Process.Start` sem ter verificado que
o processo filho de fato existe/funciona em uma máquina limpa, e não declarar essa feature como
"concluída" em `docs/STATUS.md` até isso ser verdade.
