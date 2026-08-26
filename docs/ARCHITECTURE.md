# Architecture

Uma nova sessão (humana ou IA) deve conseguir entender o sistema lendo só este arquivo, mais
[`DECISIONS.md`](DECISIONS.md) para o porquê de cada escolha relevante.

## Princípio central

A UI (`TS4Translator.App`) nunca conhece detalhes de LlamaLogic.Packages, DBPF, STBL, ou de qual
provider de tradução está em uso. Ela depende só de interfaces definidas em `TS4Translator.Core`.
Isso é reforçado fisicamente: `Core` não tem `PackageReference` para nenhuma biblioteca de terceiros
além do BCL, e nenhum outro projeto pode remover essa restrição sem editar `Core.csproj`.

```text
TS4Translator.App (WinUI 3, ViewModels, DI)
        │  depende de
        ▼
TS4Translator.Core (interfaces + modelos de domínio, zero dependências externas)
        ▲                    ▲                    ▲
        │ implementa         │ implementa         │ implementa
TS4Translator.Sims4   TS4Translator.Translation   TS4Translator.Providers
   (LlamaLogic.Packages)   (TokenProtector,          (Argos, NVIDIA NIM,
                             Validator, Batching)      OpenAI-compatible, ...)
        │
        ▼
TS4Translator.Persistence (SQLite)     TS4Translator.Infrastructure (logging, credenciais, HTTP)
```

`Providers` referencia `Translation` (não o contrário) porque providers concretos usam o
`ITokenProtector`/contratos de tradução definidos ali para proteger o texto antes de chamar a API
externa — a orquestração de proteger→traduzir→restaurar é responsabilidade de quem chama o provider,
tipicamente um serviço em `Translation`, não do provider em si.

## Projetos

### TS4Translator.Core

Interfaces e DTOs puros. Nenhuma implementação, nenhuma dependência de terceiros. Organizado por
área funcional em subpastas:

- `Tokens/` — `ITokenProtector`, `ProtectedString`, `ProtectedToken`, `TokenRestoreResult`,
  `TokenValidationIssue(Kind)`. Ver DEC-004 para o design de proteção/restauração.
- `Validation/` — `ITranslationValidator`, `ValidationIssue`, `ValidationSeverity` (Info/Warning/Error/Critical),
  `ValidationIssueCode`.
- (Planejado, ainda não criado) `Packages/`, `Translation/` (provider/job contracts), `Memory/`,
  `Glossary/`, `Projects/` — serão adicionados quando as fases correspondentes do roadmap começarem,
  não antecipadamente (ver seção 60 do brief original: evitar abstração antes de haver uma implementação
  real para validá-la).

### TS4Translator.Translation

Implementações que não dependem de nenhum SDK de terceiros nem de I/O:

- `TokenProtector : ITokenProtector` — extração por regex de tokens `{...}`, tags `<...>` e quebras
  de linha; reconciliação por identidade de placeholder. Ver DEC-004.
- `TranslationValidator : ITranslationValidator` — funde os problemas do `TokenRestoreResult` com
  checagens próprias (vazio, idêntico ao original, discrepância de tamanho, markup desbalanceado).

Planejado aqui: `BatchBuilder` (Fase 3), `TranslationOrchestrator` (protege → chama provider →
restaura → valida, por ID, nunca por posição — contrato descrito na seção 10 do brief), perfis de
tradução (seção 22).

### TS4Translator.Sims4

Ainda vazio. Vai encapsular `LlamaLogic.Packages` (referenciado apenas aqui) atrás de
`IPackageService`/`IStringTableService` definidos em `Core`. Nenhum tipo de LlamaLogic deve vazar
para fora deste projeto — ver o princípio central acima e a seção 6 do brief.

Bloqueado em pesquisa de fatos (locale encoding no Instance ID de STBL, tipo `StringTableResource`
exato exposto pela biblioteca) antes de escrever código real — ver `docs/STATUS.md`.

### TS4Translator.Providers

Ainda vazio. Vai conter um `ITranslationProvider` por mecanismo de tradução (Argos, NVIDIA NIM,
OpenAI-compatible, DeepL, etc.), cada um isolado o suficiente para uma falha ou mudança de API de
um provider não afetar os outros nem o core do app (seção 18 do brief).

### TS4Translator.Persistence

Ainda vazio. SQLite com schema versionado (migrations) para as entidades da seção 37 do brief
(Project, StringEntry, Translation, TranslationMemory, GlossaryTerm, TranslationJob, etc.). API keys
**nunca** ficam aqui em texto puro — isso é responsabilidade de `Infrastructure` via Windows
Credential Manager/DPAPI (seção 19).

### TS4Translator.Infrastructure

Ainda vazio. Logging estruturado, armazenamento seguro de credenciais, `HttpClientFactory` para
providers online.

### TS4Translator.App

Gerado a partir do template `dotnet new winui-navview` (DEC-005), depois com o namespace corrigido
de `TS4Translator_App` para `TS4Translator.App`. Hoje é só a shell do template (NavigationView +
Home/About/Settings) sem lógica de negócio conectada — DI, MVVM real e navegação para as telas do
produto (import, editor, dashboard) ainda não foram implementados.

## Convenções de qualidade

- `Directory.Build.props` na raiz aplica `TreatWarningsAsErrors`, `EnableNETAnalyzers`,
  `AnalysisLevel=latest-recommended` a todo o repositório.
- `tests/Directory.Build.props` importa o da raiz e adiciona `NoWarn=CA1707` só para projetos de
  teste, para permitir a convenção `Metodo_Cenario_Resultado` em nomes de teste (DEC-003).
- Toda decisão não-trivial (troca de biblioteca, formato de arquivo, contrato de interface que afeta
  múltiplas camadas) é registrada em `DECISIONS.md` antes de seguir em frente.
