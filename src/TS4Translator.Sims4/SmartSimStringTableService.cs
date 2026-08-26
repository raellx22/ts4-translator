using System.Globalization;
using LlamaLogic.Packages;
using TS4Translator.Core.Packages;

namespace TS4Translator.Sims4;

/// <summary>
/// <see cref="IStringTableService"/> backed by <c>LlamaLogic.Packages.SmartSimUtilities</c>, which
/// already implements The Sims 4's locale-in-top-instance-byte encoding for STBL resources. This
/// class only converts between our own <see cref="StblKey"/> and LlamaLogic's <see cref="ResourceKey"/> —
/// it deliberately does not reimplement the locale bit math itself (see docs/DECISIONS.md).
/// </summary>
public sealed class SmartSimStringTableService : IStringTableService
{
    public StblKey CreateKey(CultureInfo locale, uint group, ulong instance)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var resourceKey = SmartSimUtilities.GetStringTableResourceKey(locale, group, instance);
        return ToStblKey(resourceKey);
    }

    public StblKey WithLocale(StblKey key, CultureInfo locale)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var retargeted = SmartSimUtilities.GetResourceKeyForLocale(ToResourceKey(key), locale);
        return ToStblKey(retargeted);
    }

    public CultureInfo GetLocale(StblKey key) => SmartSimUtilities.GetStringTableLocale(ToResourceKey(key));

    internal static ResourceKey ToResourceKey(StblKey key) => new(ResourceType.StringTable, key.Group, key.FullInstance);

    private static StblKey ToStblKey(ResourceKey key) => new(key.Group, key.FullInstance);
}
