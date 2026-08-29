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
