using TS4Translator.Core.Translation;

namespace TS4Translator.Providers.Argos;

public interface IArgosRuntimeManager
{
    Task<ArgosRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ArgosRuntimeStatus> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(
        IReadOnlyList<TranslationRequestItem> items,
        string sourceCode,
        string targetCode,
        CancellationToken cancellationToken = default);
}
