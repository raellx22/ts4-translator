# Argos Translate — provider offline implementado

O Argos é um provider local de primeira classe do TS4 Translator. Ele não exige uma instalação de
Python do usuário, API key, servidor local ou GPU. A tela Configurações baixa e mantém um runtime
isolado em `%LOCALAPPDATA%\TS4Translator\Models\Argos`, instala o modelo direto inglês → português do
Brasil e permite selecionar `Argos Translate (offline)` como provider ativo.

## Componentes e versões fixadas

- Python 3.12.10 embeddable x64, baixado de `python.org` e validado por SHA-256 fixado.
- Argos Translate 1.11.0 e CTranslate2 4.8.1.
- Modelo oficial `translate-en_pb-1_9.argosmodel` (`en` → `pb`), validado por SHA-256.
- Sidecar persistente por stdin/stdout, com mensagens JSONL e correlação por `id`.

Argos 1.11 declara Stanza e spaCy mesmo quando o caminho MiniSBD é escolhido. Instalar o grafo padrão
traria PyTorch e centenas de megabytes não usados. O instalador do app fixa `ARGOS_CHUNK_TYPE=MINISBD`,
instala apenas CTranslate2/MiniSBD e suas dependências e fornece um módulo Stanza inerte para satisfazer
o import obrigatório. O runtime medido ocupa aproximadamente 221 MB e o modelo instalado 83 MB; os
arquivos compactados baixados somam aproximadamente 79 MB e ficam no cache para reparo.

## Por que um sidecar

O Python fica isolado do processo WinUI. `ArgosRuntimeManager` inicia uma única instância sem janela,
mantém o modelo carregado entre lotes, força UTF-8 no Windows, captura erros e encerra a árvore do
processo com o app. `ArgosTranslateProvider` continua obedecendo ao mesmo `ITranslationProvider` dos
providers HTTP, com `SupportsLocalExecution=true`, `RequiresApiKey=false`, concorrência 1 e batches
recomendados de 256 itens/64.000 caracteres.

## Otimização que fechou o gap

A API pública do Argos recebe uma string por tradução. Em STBLs, isso produz milhares de chamadas
CTranslate2 quase vazias. O sidecar fixa a versão 1.11 e reutiliza seu tokenizador, sentencizador e
decoder, mas agrega as sentenças de até 256 entradas em uma única chamada `translate_batch`. Se a
estrutura interna esperada não estiver disponível, ele volta para a API pública.

Tokens do The Sims são primeiro convertidos em caracteres Unicode privados. Se qualquer placeholder
for perdido, duplicado ou alterado, o sidecar repete somente essa entrada por uma rota determinística:
traduz os segmentos de linguagem natural entre tokens e intercala os placeholders originais. O
`TokenProtector` também cobre tags, quebras reais e escapadas (`\n`/`\r\n`), URLs e handles `@nome`.

## Validação real em Windows

O host usado não tinha `python`, `py` ou Argos no PATH. O bootstrap embarcável, a instalação enxuta,
o import de Argos/CTranslate2, a instalação do modelo, o processo persistente e uma tradução foram
executados com sucesso. O comando de instalação do próprio `ArgosRuntimeManager` também foi exercitado
de ponta a ponta.

No `TURBODRIVER_WickedWhims_Tuning.package` real:

- 6.779 strings inglesas, 440.402 caracteres, cinco STBLs de origem;
- amostra estável de 500 entradas: 22,74 s, projeção de 308,33 s (5,14 min) para o corpus;
- 496/500 aprovadas automaticamente, zero ausentes e zero falhas estruturais;
- quatro revisões eram somente `IdenticalToSource` (nomes próprios/frases que o modelo manteve).
- execução integral: 268,60 s (4 min 28,6 s), 6.779/6.779 respostas, 6.674 aprovadas e 105 para
  revisão; os 105 casos eram 102 `IdenticalToSource` e três `LengthDiscrepancy`, sem erro crítico.

Os relatórios reproduzíveis são `benchmark-results/argos-wickedwhims-500-final.json` e
`benchmark-results/argos-wickedwhims-full.json` (diretório ignorado no Git por conter texto de mod de
terceiros).

## Limites conhecidos

- Argos é muito rápido e gratuito, mas a naturalidade é inferior aos melhores modelos pagos. A
  amostra contém escolhas literais e terminologia que merece revisão humana.
- Nesta versão só existe o par direto `en → pt-BR`; outros pares precisam de UI/manifesto próprios.
- O sidecar otimizado depende de detalhes internos da versão Argos 1.11, por isso as versões são
  fixadas e upgrades exigem repetir testes estruturais e de desempenho.
- O runtime foi validado fora do sandbox e o app x64 Release compila. Ainda é obrigatório validar a
  instalação/execução a partir do MSIX final em um Windows limpo antes de declarar distribuição pronta.
- O instalador é sob demanda e requer internet apenas na primeira instalação ou reparo. Depois disso,
  a tradução é totalmente offline.
