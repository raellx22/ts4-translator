# TS4 Translator

Aplicativo desktop WinUI 3 para traduzir mods `.package` do The Sims 4: importar, extrair strings
STBL, traduzir (offline com Argos Translate, ou via NVIDIA NIM / providers compatíveis com OpenAI /
outros), proteger tokens como `{0.SimFirstName}` contra corrupção pelo tradutor, validar, revisar, e
exportar um package de tradução separado — sem jamais alterar o mod original.

## Estado do projeto

**O caminho prático completo já funciona de ponta a ponta**, verificado rodando o app instalado de
verdade: importar um `.package` → extrair cada string → traduzir as pendentes (com um provider
compatível com OpenAI configurado) → exportar um package traduzido com validação de round-trip real.
A chamada HTTP de tradução em si já foi testada pelo usuário contra a NVIDIA NIM de verdade, o que
revelou e corrigiu dois problemas reais: uma resposta com `"translation": null` derrubando o lote
inteiro, e falta de feedback de progresso/salvamento incremental em traduções grandes (ver
[`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-017/DEC-018). Um incidente de desenvolvimento apagou essa
configuração depois (DEC-020, já corrigido para não repetir) — o usuário precisa reconfigurar o
provider antes de continuar. Ver [`docs/STATUS.md`](docs/STATUS.md) para o estado exato e
[`docs/HANDOFF.md`](docs/HANDOFF.md) para retomar o trabalho em uma nova sessão.

Implementado e testado até agora (632 testes automatizados, todos passando nesta máquina):

- Solução .NET 10 / WinUI 3 completa (`TS4Translator.sln`), com App, seis bibliotecas de camada e
  cinco projetos de teste, compilando limpo em x64.
- Shell WinUI com `NavigationView`, título estendido e Mica (a partir do template `winui-navview`,
  ver [`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-005), com DI (`ServiceCollection`) no composition
  root.
- **Home**: importar `.package` (múltiplos arquivos, via seletor ou arrastando para a janela), extrair
  e persistir cada string de cada STBL, criar um `TranslationProject`, listar recentes. Reimportar um
  mod já processado mostra quantas strings são novas/alteradas/reaproveitadas/removidas.
- **Editor**: lista as strings de um projeto com filtro por status e busca de texto; instruções de
  estilo/tom livres por projeto (Translation Profiles, seção 22); "Traduzir pendentes" roda o pipeline
  de tradução completo (aplicando o glossário e as instruções do projeto) em blocos de 100 strings,
  cada um salvo antes do próximo, com barra de progresso real e botão de cancelar seguro; edição
  manual da tradução é salva com histórico; "Exportar" gera um package traduzido e só reporta sucesso
  depois de reabrir o arquivo e validar cada string (round-trip real, seção 28 do brief).
  "Retraduzir do zero" descarta as traduções do projeto, esquece o que elas ensinaram à memória e roda
  tudo de novo pelo motor — o caminho para testar outro modelo no mesmo mod, que antes não existia
  (ver [`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-038). A caixa "Reaproveitar a memória" faz o mesmo
  pontualmente, e o fim da tradução agora diz quantas linhas vieram da memória sem passar por motor.
- **Fila de suspeitas** (DEC-040): o filtro "Suspeitas" separa as traduções que passaram na validação
  mas parecem ter perdido pedaço do original — títulos sem a formatação, frases que viraram uma só,
  rótulos curtos que perderam o termo do jogo (`Attractiveness Tuner` → `Atraente`). As regras foram
  lidas do banco real do usuário e marcam 2,7% das traduções: 2,8% do que veio de motor contra 0,7% do
  que foi escrito à mão. "Repensar" — na linha ou em lote — devolve só essas ao motor escolhido **com o
  raciocínio do modelo ligado**, sem alterar o perfil salvo.
- **Glossário**: termos globais (nomes próprios, marcas — nunca traduzir, ou sempre traduzir de um
  jeito específico), aplicados automaticamente durante a tradução. Tela própria para adicionar/remover
  termos globais; termos por projeto existem no serviço mas ainda sem UI dedicada. Vem com **386 termos
  padrão** minerados das traduções que o usuário aprovou à mão, cada um validado contra o resto do
  corpus: medido em sete modelos locais, o glossário **praticamente dobrou o acerto exato de todos**
  (de 5–19% para 17–31%) e é o maior ganho isolado já medido no projeto — ver
  [`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-043. O casamento passou a ser por palavra inteira, sem o
  qual `Male → Masculino` dispararia dentro de `Female`.
- **Configurações**: seletor de tipo de provider (Argos offline, OpenAI-compatible, DeepL ou Google
  Gemini); providers HTTP têm API key criptografada via DPAPI, e o Argos oferece instalação/reparo do
  runtime e modelo locais sem exigir Python global. O motor local (`.gguf` rodando dentro do app) tem
  um seletor de **raciocínio** — desligado, ligado, ou como o modelo vier — e a tela lê o cabeçalho do
  próprio arquivo para dizer se aquele modelo tem raciocínio antes de você gastar uma tradução
  descobrindo (DEC-039).
- Fluxo completo confirmado rodando o app instalado como MSIX, com um `.package` sintético — ver
  DEC-011 e [`docs/BUILD.md`](docs/BUILD.md) para como testar isso você mesmo.
- `TokenProtector`: protege tokens `{0.SimFirstName}`/`{M0.He}`/`{F0.She}`, tags de markup, URLs,
  handles e quebras
  de linha antes de qualquer tradução, e reconcilia o resultado por identidade de placeholder (não por
  ordem). 27 casos de teste.
- `TranslationValidator`/`BatchBuilder`/`TranslationOrchestrator`: pipeline completo
  memory (exata→fuzzy≥97%)→protect→provider→reconciliação por ID→restore→validate, nunca confiando na
  ordem/contagem dos resultados do provider. Um reaproveitamento fuzzy sempre volta marcado para
  revisão, nunca como sucesso silencioso. 35 testes ao todo cobrindo essas peças.
- `StringSimilarity` (Levenshtein): base do fuzzy matching acima. 7 testes isolados.
- `Sims4PackageService`/`SmartSimStringTableService`: leitura/escrita real de STBL via
  `LlamaLogic.Packages` (inclusive múltiplas STBLs num único package de saída), locale (incluindo
  PT-BR) via `SmartSimUtilities`, nunca sobrescrevendo o package original. 13 testes.
- `ArgosTranslateProvider`, `OpenAICompatibleProvider`, `DeepLProvider`, `GoogleGeminiProvider`: quatro
  providers reais selecionáveis em Configurações (`ProviderKind`/`ConfiguredProviderFactory`), com recuperação
  controlada de JSON truncado/malformado onde aplicável. `GoogleGeminiProvider` foi validado contra a
  API real e num corpus real do WickedWhims; Argos foi validado localmente no mesmo corpus, com
  6.779 strings em 4 min 28,6 s e zero falhas estruturais; DeepL continua apenas em HTTP falso.

Ainda não implementado: DeepL validado com API key real, OpenAI
dedicado e Claude como providers, UI de glossário por projeto (só o escopo global tem tela), fuzzy
matching navegável no Editor (a API já existe, só falta a tela), biblioteca de perfis de tradução
nomeados/reutilizáveis, empacotamento MSIX de distribuição. Ver [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Por que este stack

O projeto irmão `../SimsPackageBatcher/`, no mesmo diretório de trabalho, já prova que este conjunto
de versões compila, empacota em MSIX e passa testes de integração neste ambiente Windows
(verificado em 26 de agosto de 2026): .NET SDK 10.0.400, Windows App SDK 2.4.0, LlamaLogic.Packages
3.8.2, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.11, xUnit 2.9.3.
TS4 Translator reaproveita exatamente essas versões — ver [`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-001.

## Compilar, testar e rodar

Ver [`docs/BUILD.md`](docs/BUILD.md) para os comandos completos — inclui o procedimento necessário
para testar fluxos que usam o seletor de arquivos do Windows (`dotnet run` sozinho não é suficiente
para isso, ver DEC-010).

## Arquitetura

Ver [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) para a explicação completa. Resumo dos projetos:

```text
TS4Translator.sln
src/
  TS4Translator.App/             WinUI 3: Views, ViewModels, DI, integração Windows
  TS4Translator.Core/            contratos (interfaces) e modelos de domínio, sem dependências externas
  TS4Translator.Sims4/           DBPF/STBL via LlamaLogic.Packages, atrás de IPackageService/IStringTableService
  TS4Translator.Translation/     TokenProtector, TranslationValidator, BatchBuilder, orquestração
  TS4Translator.Providers/       implementações de ITranslationProvider (Argos, OpenAI-compatible, DeepL, Google Gemini)
  TS4Translator.Persistence/     SQLite: projetos, translation memory, glossário, jobs
  TS4Translator.Infrastructure/  logging, credenciais (Windows Credential Manager/DPAPI), HTTP
tests/
  TS4Translator.Core.Tests/
  TS4Translator.Sims4.Tests/
  TS4Translator.Translation.Tests/
  TS4Translator.Providers.Tests/
  TS4Translator.IntegrationTests/
docs/
```

## Documentação

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — como as camadas se encaixam.
- [`docs/DECISIONS.md`](docs/DECISIONS.md) — decisões arquiteturais registradas (ADRs).
- [`docs/BUILD.md`](docs/BUILD.md) — compilar, testar, rodar, e empacotar para testar o file picker.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — fases de implementação e o que falta.
- [`docs/STATUS.md`](docs/STATUS.md) — estado atual, bloqueios, próximos passos.
- [`docs/HANDOFF.md`](docs/HANDOFF.md) — como retomar este projeto em uma nova sessão/agente.
- [`docs/ARGOS.md`](docs/ARGOS.md) — runtime, sidecar, otimização, benchmark e limites do Argos offline.
