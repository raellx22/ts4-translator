using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.Argos;

/// <summary>Installs and owns the persistent app-managed Argos Python sidecar.</summary>
public sealed class ArgosRuntimeManager(ArgosRuntimeOptions options) : IArgosRuntimeManager, IAsyncDisposable
{
    private const string PythonUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
    private const string PythonSha256 = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private const string GetPipSha256 = "fb24e693bab954209a063d90953621412ccad4a500905a726286e038f508ddf6";
    private const string ModelUrl = "https://argos-net.com/v1/translate-en_pb-1_9.argosmodel";
    private const string ModelSha256 = "1d1cd5e9540c6b38c258bed002a42d3b311b8a189acb74feaa311ef30d175c5b";
    private const string SidecarResourceName = "TS4Translator.Providers.Argos.sidecar.py";
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly SemaphoreSlim installLock = new(1, 1);
    private readonly SemaphoreSlim protocolLock = new(1, 1);
    private Process? process;
    private HttpClient? sidecarClient;
    private long nextRequestId;

    public async Task<ArgosRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.PythonExecutable))
        {
            return new(false, false, "Argos ainda não está instalado.");
        }

        try
        {
            using var response = await SendCommandAsync(new { command = "status" }, cancellationToken).ConfigureAwait(false);
            var modelInstalled = response.RootElement.GetProperty("packages").EnumerateArray().Any(package =>
                package.GetProperty("from").GetString() == "en" && package.GetProperty("to").GetString() == "pb");
            return new(true, modelInstalled, modelInstalled
                ? "Argos en→pt-BR está instalado e pronto para uso."
                : "Runtime Argos instalado; falta instalar o modelo en→pt-BR.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, false, $"Runtime Argos inválido: {exception.Message}");
        }
    }

    public async Task<ArgosRuntimeStatus> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(options.RootDirectory);
            Directory.CreateDirectory(options.DownloadsDirectory);

            if (!File.Exists(options.ReadyMarkerPath) || !File.Exists(options.PythonExecutable))
            {
                await StopProcessAsync().ConfigureAwait(false);
                progress?.Report("Baixando o Python embarcável (10,6 MB)...");
                var pythonArchive = Path.Combine(options.DownloadsDirectory, "python-3.12.10-embed-amd64.zip");
                await DownloadAndVerifyAsync(PythonUrl, pythonArchive, HashAlgorithmName.SHA256, PythonSha256, cancellationToken).ConfigureAwait(false);

                var pythonDirectory = Path.GetDirectoryName(options.PythonExecutable)!;
                Directory.CreateDirectory(pythonDirectory);
                ZipFile.ExtractToDirectory(pythonArchive, pythonDirectory, overwriteFiles: true);
                var pthPath = Path.Combine(pythonDirectory, "python312._pth");
                var pth = await File.ReadAllTextAsync(pthPath, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(pthPath, pth.Replace("#import site", "import site", StringComparison.Ordinal), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

                progress?.Report("Preparando o instalador Python...");
                var getPipPath = Path.Combine(options.DownloadsDirectory, "get-pip.py");
                await DownloadAndVerifyAsync(GetPipUrl, getPipPath, HashAlgorithmName.SHA256, GetPipSha256, cancellationToken).ConfigureAwait(false);
                await RunPythonAsync([getPipPath, "--disable-pip-version-check"], cancellationToken).ConfigureAwait(false);

                progress?.Report("Instalando o runtime enxuto Argos/CTranslate2 (aprox. 210 MB)...");
                await RunPythonAsync([
                    "-m", "pip", "install", "--disable-pip-version-check", "--no-warn-script-location",
                    "ctranslate2==4.8.1", "minisbd==0.9.5", "packaging==26.3", "sacremoses==0.1.1",
                    "sentencepiece==0.2.2", "requests==2.34.2", "pyyaml==6.0.3",
                ], cancellationToken).ConfigureAwait(false);
                await RunPythonAsync([
                    "-m", "pip", "install", "--disable-pip-version-check", "--no-warn-script-location", "--no-deps",
                    "argostranslate==1.11.0",
                ], cancellationToken).ConfigureAwait(false);

                await WriteSidecarAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(options.ReadyMarkerPath, "argostranslate=1.11.0\nctranslate2=4.8.1\n", cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteSidecarAsync(cancellationToken).ConfigureAwait(false);
            }

            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!status.RuntimeInstalled)
            {
                File.Delete(options.ReadyMarkerPath);
                throw new InvalidOperationException(status.Message);
            }

            if (!status.EnglishToBrazilianPortugueseInstalled)
            {
                progress?.Report("Baixando o modelo Argos inglês → português do Brasil (66 MB)...");
                var modelArchive = Path.Combine(options.DownloadsDirectory, "translate-en_pb-1_9.argosmodel");
                await DownloadAndVerifyAsync(ModelUrl, modelArchive, HashAlgorithmName.SHA256, ModelSha256, cancellationToken).ConfigureAwait(false);
                progress?.Report("Instalando e validando o modelo en→pt-BR...");
                using var response = await SendCommandAsync(new { command = "install_model", path = modelArchive }, cancellationToken).ConfigureAwait(false);
            }

            var finalStatus = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!finalStatus.IsReady)
            {
                throw new InvalidOperationException(finalStatus.Message);
            }

            progress?.Report(finalStatus.Message);
            return finalStatus;
        }
        finally
        {
            installLock.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(
        IReadOnlyList<TranslationRequestItem> items,
        string sourceCode,
        string targetCode,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var requestItems = items.Select(item => new { id = item.Id, text = item.ProtectedText }).ToArray();
        using var response = await SendCommandAsync(new
        {
            command = "translate",
            source = sourceCode,
            target = targetCode,
            workers = 1,
            items = requestItems,
        }, cancellationToken).ConfigureAwait(false);

        var sources = items.ToDictionary(item => item.Id, item => item.ProtectedText, StringComparer.Ordinal);
        return response.RootElement.GetProperty("items").EnumerateArray()
            .Select(item =>
            {
                var id = item.GetProperty("id").GetString()!;
                var translated = item.GetProperty("text").GetString()!;
                return new TranslationResultItem(id, NormalizeTokenBoundaryWhitespace(sources[id], translated));
            })
            .ToList();
    }

    private static string NormalizeTokenBoundaryWhitespace(string source, string translated)
    {
        foreach (Match match in Regex.Matches(source, @"__TS4_TOKEN_\d{4}__"))
        {
            var token = match.Value;
            var sourceHasSpaceBefore = match.Index > 0 && char.IsWhiteSpace(source[match.Index - 1]);
            var sourceHasSpaceAfter = match.Index + match.Length < source.Length && char.IsWhiteSpace(source[match.Index + match.Length]);
            var translatedIndex = translated.IndexOf(token, StringComparison.Ordinal);
            if (translatedIndex < 0)
            {
                continue;
            }
            if (sourceHasSpaceBefore && translatedIndex > 0 && !char.IsWhiteSpace(translated[translatedIndex - 1]))
            {
                translated = translated.Insert(translatedIndex, " ");
                translatedIndex++;
            }
            var afterIndex = translatedIndex + token.Length;
            if (sourceHasSpaceAfter && afterIndex < translated.Length && !char.IsWhiteSpace(translated[afterIndex]))
            {
                translated = translated.Insert(afterIndex, " ");
            }
        }
        return translated;
    }

    private async Task<JsonDocument> SendCommandAsync(object command, CancellationToken cancellationToken)
    {
        await protocolLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureProcessAsync(cancellationToken).ConfigureAwait(false);
            var requestId = Interlocked.Increment(ref nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var json = JsonSerializer.Serialize(command);
            using var document = JsonDocument.Parse(json);
            var request = new Dictionary<string, object?> { ["id"] = requestId };
            foreach (var property in document.RootElement.EnumerateObject())
            {
                request[property.Name] = property.Value.Clone();
            }

            using var content = new StringContent(JsonSerializer.Serialize(request), new UTF8Encoding(false), "application/json");
            using var message = await sidecarClient!.PostAsync("command", content, cancellationToken).ConfigureAwait(false);
            message.EnsureSuccessStatusCode();
            await using var responseStream = await message.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var response = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.RootElement.GetProperty("id").GetString() != requestId)
            {
                response.Dispose();
                throw new InvalidOperationException("O protocolo Argos perdeu a sincronização entre requisição e resposta.");
            }
            if (!response.RootElement.GetProperty("ok").GetBoolean())
            {
                var error = response.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Erro desconhecido.";
                response.Dispose();
                throw new InvalidOperationException($"Falha no Argos: {error}");
            }
            return response;
        }
        catch
        {
            // A canceled request can leave native translation work running. Restarting guarantees that
            // the next request cannot observe stale state from the canceled operation.
            await StopProcessAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            protocolLock.Release();
        }
    }

    private async Task EnsureProcessAsync(CancellationToken cancellationToken)
    {
        if (process is { HasExited: false } && sidecarClient is not null)
        {
            return;
        }
        if (!File.Exists(options.PythonExecutable))
        {
            throw new InvalidOperationException("Argos não está instalado. Instale-o em Configurações antes de traduzir.");
        }

        await WriteSidecarAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(options.PackagesDirectory);
        File.Delete(options.EndpointMarkerPath);
        File.Delete(options.EndpointMarkerPath + ".partial");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var startInfo = new ProcessStartInfo(options.PythonExecutable)
        {
            // See RunPythonAsync: the desktop shell broker prevents the downloaded runtime from
            // inheriting the MSIX package graph. HTTP over loopback replaces redirected stdio because
            // ShellExecute and anonymous-pipe redirection are mutually exclusive.
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(options.SidecarPath);
        startInfo.ArgumentList.Add("--server");
        startInfo.ArgumentList.Add("--port-file");
        startInfo.ArgumentList.Add(options.EndpointMarkerPath);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add(token);
        startInfo.ArgumentList.Add("--packages-dir");
        startInfo.ArgumentList.Add(options.PackagesDirectory);
        startInfo.ArgumentList.Add("--runtime-root");
        startInfo.ArgumentList.Add(options.RootDirectory);

        process?.Dispose();
        process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Não foi possível iniciar o sidecar Argos.");
        }

        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (!File.Exists(options.EndpointMarkerPath))
            {
                if (process.HasExited)
                {
                    var code = unchecked((uint)process.ExitCode);
                    throw new InvalidOperationException($"O sidecar Argos encerrou durante a inicialização (0x{code:X8}).");
                }
                await Task.Delay(100, startupTimeout.Token).ConfigureAwait(false);
            }

            await using var markerStream = new FileStream(options.EndpointMarkerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var marker = await JsonDocument.ParseAsync(markerStream, cancellationToken: startupTimeout.Token).ConfigureAwait(false);
            var port = marker.RootElement.GetProperty("port").GetInt32();
            sidecarClient?.Dispose();
            sidecarClient = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = TimeSpan.FromMinutes(30),
            };
            sidecarClient.DefaultRequestHeaders.Add("X-TS4-Argos-Token", token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("O sidecar Argos não ficou pronto em 60 segundos.");
        }
    }

    private async Task WriteSidecarAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RootDirectory);
        await using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(SidecarResourceName)
            ?? throw new InvalidOperationException($"Recurso embarcado ausente: {SidecarResourceName}.");
        await using var destination = new FileStream(options.SidecarPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadAndVerifyAsync(string url, string destination, HashAlgorithmName algorithm, string expectedHash, CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && await HashMatchesAsync(destination, algorithm, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var partial = destination + ".partial";
        using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        if (!await HashMatchesAsync(partial, algorithm, expectedHash, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Falha na verificação de integridade do download: {Path.GetFileName(destination)}.");
        }
        File.Move(partial, destination, overwrite: true);
    }

    private static async Task<bool> HashMatchesAsync(string path, HashAlgorithmName algorithm, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunPythonAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.PythonExecutable)
        {
            // A full MSIX desktop process has package identity. CreateProcess causes a downloaded
            // Python child to inherit that package graph, and the Windows loader then terminates the
            // child with STATUS_DLL_NOT_FOUND (0xC0000135) before Python can start. ShellExecute uses
            // the desktop shell broker, so the app-managed runtime starts as an ordinary unpackaged
            // process and can load its adjacent python312.dll and native wheels normally.
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var child = Process.Start(startInfo) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador Python.");
        try
        {
            await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        if (child.ExitCode != 0)
        {
            var unsignedExitCode = unchecked((uint)child.ExitCode);
            throw new InvalidOperationException(
                $"Instalação Argos falhou (código {child.ExitCode}, 0x{unsignedExitCode:X8}).");
        }
    }

    private async Task StopProcessAsync()
    {
        if (sidecarClient is not null && process is { HasExited: false })
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var content = new StringContent("{\"id\":\"shutdown\",\"command\":\"shutdown\"}", Encoding.UTF8, "application/json");
                using var response = await sidecarClient.PostAsync("command", content, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best effort only; the process-tree kill below is the deterministic fallback.
            }
        }
        if (process is { HasExited: false })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        sidecarClient?.Dispose();
        sidecarClient = null;
        process?.Dispose();
        process = null;
        File.Delete(options.EndpointMarkerPath);
        File.Delete(options.EndpointMarkerPath + ".partial");
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessAsync().ConfigureAwait(false);
        installLock.Dispose();
        protocolLock.Dispose();
    }
}
