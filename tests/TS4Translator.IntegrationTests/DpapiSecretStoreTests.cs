using System.Runtime.Versioning;
using TS4Translator.Infrastructure;

namespace TS4Translator.IntegrationTests;

/// <summary>Exercises the real Windows DPAPI encryption, in an isolated temp directory (never touches the app's real secrets folder).</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-secrets-tests-").FullName;
    private readonly DpapiSecretStore _sut;

    public DpapiSecretStoreTests()
    {
        _sut = new DpapiSecretStore(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_KeyNeverSaved_ReturnsNull()
    {
        Assert.Null(_sut.Load("nonexistent"));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheSecret()
    {
        _sut.Save("nvidia-api-key", "nvapi-abc123");

        Assert.Equal("nvapi-abc123", _sut.Load("nvidia-api-key"));
    }

    [Fact]
    public void Save_WritesCiphertextNotPlaintextToDisk()
    {
        _sut.Save("nvidia-api-key", "nvapi-abc123");

        var files = Directory.GetFiles(_tempDirectory);
        var fileContent = File.ReadAllText(files[0]);
        Assert.DoesNotContain("nvapi-abc123", fileContent);
    }

    [Fact]
    public void Delete_RemovesTheSecret()
    {
        _sut.Save("nvidia-api-key", "nvapi-abc123");

        _sut.Delete("nvidia-api-key");

        Assert.Null(_sut.Load("nvidia-api-key"));
    }

    [Fact]
    public void Delete_KeyNeverSaved_DoesNotThrow()
    {
        _sut.Delete("nonexistent");
    }

    [Fact]
    public void Save_KeyWithUnsafeCharacters_DoesNotThrow()
    {
        _sut.Save("provider:openai-compatible", "value");

        Assert.Equal("value", _sut.Load("provider:openai-compatible"));
    }
}
