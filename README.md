# TS4 Translator

Aplicativo desktop WinUI 3 para traduzir mods `.package` do The Sims 4: importar, extrair strings
STBL, traduzir (offline com Argos Translate, ou via NVIDIA NIM / providers compatíveis com OpenAI /
outros), proteger tokens como `{0.SimFirstName}` contra corrupção pelo tradutor, validar, revisar, e
exportar um package de tradução separado — sem jamais alterar o mod original.

## Estado do projeto

**Em desenvolvimento inicial (Fase 1/2/3).** A primeira tela de produto (importar um mod, ver a
contagem de strings, criar um projeto) já funciona de ponta a ponta, verificado rodando o app de
verdade — mas ainda não há tradução, editor, nem exportação. Ver [`docs/STATUS.md`](docs/STATUS.md)
para o estado exato e [`docs/HANDOFF.md`](docs/HANDOFF.md) para retomar o trabalho em uma nova sessão.

Implementado e testado até agora (81 testes automatizados, todos passando):

- Solução .NET 10 / WinUI 3 completa (`TS4Translator.sln`), com App, seis bibliotecas de camada e
  cinco projetos de teste, compilando limpo em x64.
- Shell WinUI com `NavigationView`, título estendido e Mica (a partir do template `winui-navview`,
  ver [`docs/DECISIONS.md`](docs/DECISIONS.md) DEC-005), com DI (`ServiceCollection`) no composition
  root. A tela **Home** já é funcional de ponta a ponta: importar `.package` → contar STBLs → criar
  `TranslationProject` → listar recentes — confirmado rodando o app de verdade, não só compilando
  (ver DEC-010 e [`docs/BUILD.md`](docs/BUILD.md) para como testar isso você mesmo).
- `IProjectService`/`EfProjectService`: CRUD de projetos com EF Core/SQLite. 7 testes de integração.
- `TokenProtector`: protege tokens `{0.SimFirstName}`/`{M0.He}`/`{F0.She}`, tags de markup e quebras
  de linha antes de qualquer tradução, e reconcilia o resultado por identidade de placeholder (não por
  ordem). 23 testes.
- `TranslationValidator`: consolida os problemas de token com verificações independentes (tradução
  vazia, idêntica ao original, discrepância de tamanho, markup desbalanceado). 12 testes.
- `Sims4PackageService`/`SmartSimStringTableService`: leitura/escrita real de STBL via
  `LlamaLogic.Packages`, locale (incluindo PT-BR) via `SmartSimUtilities`, nunca sobrescrevendo o
  package original. 11 testes com packages sintéticos.
- `SqliteTranslationMemory`: EF Core + SQLite com migration real, busca exata por texto+par de
  locales — a base da funcionalidade de reaproveitar traduções ao atualizar um mod (seção 63). 5 testes
  de integração.
- `ITranslationOrchestrator`/`BatchBuilder`: pipeline memory→protect→provider→reconciliação por
  ID→restore→validate, nunca confiando na ordem/contagem dos resultados do provider. 14 testes.
- `OpenAICompatibleProvider`: primeiro provider real (LM Studio, NVIDIA NIM e qualquer endpoint
  compatível com OpenAI), com recuperação controlada de JSON truncado/malformado. 9 testes contra um
  HTTP falso, nenhuma chamada de rede real.

Ainda não implementado: Argos Translate, glossário, projetos, editor de strings, fuzzy matching,
exportação ponta a ponta, empacotamento MSIX de distribuição. Ver [`docs/ROADMAP.md`](docs/ROADMAP.md).

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
  TS4Translator.Providers/       implementações de ITranslationProvider (Argos, NVIDIA NIM, OpenAI-compatible, ...)
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
