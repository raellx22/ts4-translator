# Build

## Compilar e testar

```powershell
dotnet restore TS4Translator.sln
dotnet build TS4Translator.sln --configuration Release -p:Platform=x64
dotnet test TS4Translator.sln --configuration Release --no-build -p:Platform=x64
```

## Rodar durante o desenvolvimento normal

```powershell
dotnet run --project src\TS4Translator.App\TS4Translator.App.csproj -p:Platform=x64
```

Isso ativa o app via um registro de pacote "solto" (dev-loop, apontando direto para a pasta de build).
**Funciona bem para desenvolvimento normal de UI** — navegação, layout, bindings, Mica, tema. **Não
funciona para testar `FileOpenPicker`/`FileSavePicker` ou qualquer outra API mediada por broker do
Windows** — falha consistentemente com `COMException 0x80004005 (E_FAIL)`, mesmo sem elevação de
privilégios. Ver `docs/DECISIONS.md` DEC-010 para o diagnóstico completo (foi confirmado gerando um
MSIX real e testando o mesmo código sem nenhuma mudança — funcionou perfeitamente).

## Testar fluxos que usam um picker (ou qualquer API de broker)

Gere e instale um pacote MSIX de desenvolvimento local:

```powershell
# 1. Gerar o pacote (Release, sem trimming/ReadyToRun — EF Core não é compatível com trimming)
dotnet build src\TS4Translator.App\TS4Translator.App.csproj `
  -p:Platform=x64 -p:Configuration=Release `
  -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:SelfContained=false

# 2. Gerar um certificado de desenvolvimento (uma vez só; Subject precisa bater com o
#    Publisher do Package.appxmanifest, "CN=TS4Translator")
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=TS4Translator" `
  -KeyUsage DigitalSignature -FriendlyName "TS4Translator Dev Cert" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "ts4dev" -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath ts4dev.pfx -Password $pwd
Import-PfxCertificate -FilePath ts4dev.pfx -CertStoreLocation "Cert:\LocalMachine\Root" -Password $pwd
Import-PfxCertificate -FilePath ts4dev.pfx -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -Password $pwd

# 3. Assinar o MSIX gerado
$msix = "src\TS4Translator.App\AppPackages\TS4Translator.App_1.0.0.0_x64_Test\TS4Translator.App_1.0.0.0_x64.msix"
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" sign /fd SHA256 /f ts4dev.pfx /p ts4dev $msix

# 4. Instalar
Add-AppxPackage -Path $msix

# 5. Rodar o app instalado
$pkg = Get-AppxPackage -Name "60A02515-8E6A-4123-8A54-E9CB7C1B7A72*"
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
```

## ⚠️ NUNCA rode `Remove-AppxPackage` antes de reinstalar uma versão nova para testar

**`Remove-AppxPackage` apaga a pasta de dados do app inteira** (`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\TS4Translator\`) — banco SQLite (todos os projetos, translation memory, glossário) e o arquivo de API key criptografado via DPAPI. Isso já causou perda de dados real (a configuração de provider do usuário, incluindo a API key da NVIDIA, foi apagada dessa forma durante o desenvolvimento).

**O procedimento correto para atualizar o pacote instalado com uma build nova, preservando os dados:**

1. Incremente o quarto componente da versão em `src/TS4Translator.App/Package.appxmanifest` (`Version="1.0.X.0"`) — o Windows só faz upgrade in-place (`Add-AppxPackage` sem `Remove-AppxPackage` antes) quando a versão do pacote novo é maior que a instalada; a mesma versão é rejeitada ou exige remover primeiro.
2. Gere e assine o MSIX normalmente (passos 1-3 acima).
3. Rode só `Add-AppxPackage -Path $msix` — **sem** `Remove-AppxPackage` antes. Isso faz um upgrade in-place e preserva `LocalCache` inteira. Verificado empiricamente: uma configuração de provider salva sobreviveu a um upgrade de `1.0.1.0` para `1.0.2.0` feito dessa forma.

`Remove-AppxPackage` só é necessário na transição única de um registro "solto" (dev-loop, via `dotnet run`) para o primeiro pacote assinado instalado (erro `0x80073CFB` sem isso) — depois desse primeiro `Add-AppxPackage`, nunca mais remova para atualizar, só incremente a versão.

Para voltar ao ciclo normal de `dotnet run` depois de terminar de testar (isso também apaga os dados do pacote instalado — normal e esperado nesse caso, já que o objetivo é sair do modo empacotado):

```powershell
Get-AppxPackage -Name "60A02515-8E6A-4123-8A54-E9CB7C1B7A72*" | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
```

Não deixe `ts4dev.pfx` nem a pasta `AppPackages/` commitados — ambos já estão listados como gerados
localmente e não pertencem ao controle de versão (o certificado é um segredo de desenvolvimento local).

## Publicar (self-contained, sem MSIX)

```powershell
dotnet publish src\TS4Translator.App\TS4Translator.App.csproj --configuration Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false
```

Isso gera um executável self-contained, mas **sem** identidade de pacote — um `.exe` empacotado como
WinUI 3/Windows App SDK não roda corretamente se executado diretamente sem essa identidade (confirmado:
falha silenciosamente, sem sequer criar uma janela). Para distribuição real, o caminho é o MSIX (ver
seção anterior) com um certificado de publicação de verdade — isso ainda não foi resolvido para release
(ver `docs/ROADMAP.md` Fase 14).

## ⚠️ A identidade `TS4Translator.ArgosDev` instalada hoje **não** vem do `Package.appxmanifest` deste repo

Descoberto em 2026-08-30. O pacote realmente registrado nesta máquina é:

```
Name            : TS4Translator.ArgosDev
PackageFullName : TS4Translator.ArgosDev_1.0.6.0_x64__xpm2g6md2ezh4
InstallLocation : src\TS4Translator.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64
```

Ou seja: é um **registro solto** (`Add-AppxPackage -Register AppxManifest.xml`) apontando direto para a
pasta de saída do build Release — não um MSIX instalado. E o `AppxManifest.xml` gerado nessa pasta tem
`Identity Name="TS4Translator.ArgosDev"`, enquanto o `src/TS4Translator.App/Package.appxmanifest` deste
repo tem `Name="60A02515-8E6A-4123-8A54-E9CB7C1B7A72"`. A string `ArgosDev` **não existe em lugar nenhum
do repositório** — o manifesto gerado foi editado à mão depois do build, e essa edição não está versionada.

**Consequência prática:** qualquer `dotnet build -c Release` regenera `AppxManifest.xml` a partir do
`Package.appxmanifest` versionado e **apaga a identidade `ArgosDev`**, deixando o registro instalado
apontando para uma pasta cujo manifesto não bate mais com ele. Antes de rebuildar em Release, decida:

- **Voltar para o fluxo documentado (MSIX assinado, identidade `60A02515-...`)** — rebuildar, gerar e
  assinar o MSIX e `Add-AppxPackage`, conforme os passos acima. Note que `ts4dev.pfx` não está mais na
  raiz do repo (é gitignored); o certificado `CN=TS4Translator` continua em `Cert:\CurrentUser\My`
  (thumbprint `8228EB96...`, válido até 2027-08-26) e pode ser reexportado.
- **Adotar `TS4Translator.ArgosDev` de vez** — então mudar o `Identity Name` no `Package.appxmanifest`
  versionado, para que o rebuild pare de destruir a edição manual.

**Os dados do app não correm risco nessa transição:** o banco vivo está em
`%LOCALAPPDATA%\TS4Translator\Data\ts4translator.db` (caminho não virtualizado, independente da
identidade do pacote). Existe também uma cópia antiga e menor dentro de
`%LOCALAPPDATA%\Packages\TS4Translator.ArgosDev_xpm2g6md2ezh4\LocalCache\Local\` — não é a que o app
vem usando; confira a data de modificação antes de assumir qual é qual.

## ⚠️ Duas pastas de dados: a cópia em `LocalCache` sombreia `%LOCALAPPDATA%\TS4Translator`

Também descoberto em 2026-08-30, e vale mais atenção do que parece.

`AppDataPaths` resolve tudo a partir de `Environment.GetFolderPath(LocalApplicationData)`. Rodando
empacotado, o redirecionamento de sistema de arquivos do MSIX **serve a cópia em
`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\TS4Translator\` sempre que ela existir**,
mesmo que os dados de verdade estejam em `%LOCALAPPDATA%\TS4Translator\`. Era exatamente esse o estado
da máquina: dados reais em `%LOCALAPPDATA%\TS4Translator\` (banco de 50 MB com o projeto WickedWhims,
runtime Argos de 377 MB) e uma cópia abandonada de uma instalação MSIX anterior em `LocalCache`
(banco de 557 KB, Argos parcial de 35 MB). O app empacotado abria a cópia velha e parecia ter perdido
todos os projetos.

**Como diagnosticar:** rode o app e veja qual `ts4translator.db-wal` aparece — o SQLite cria o `-wal`
ao lado do banco que ele realmente abriu.

**Resolvido nesta máquina** renomeando a cópia velha para
`...\LocalCache\Local\TS4Translator.stale-backup` (nada foi apagado). Com ela fora do caminho, o app
empacotado passa a enxergar `%LOCALAPPDATA%\TS4Translator\` — banco e runtime Argos corretos. Se algum
dia essa pasta de backup não for mais necessária, ela pode ser apagada.

**Nota relacionada:** rodar `TS4Translator.App.exe` direto da pasta de saída **não funciona** enquanto
existe um registro de pacote para ela — o processo morre imediatamente com exit code `0xE0434352`
(exceção .NET não tratada, WinUI sem contexto de pacote). O caminho de execução é sempre via AUMID:

```powershell
$pkg = Get-AppxPackage -Name "TS4Translator.ArgosDev"
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
```
