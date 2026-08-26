using System.Globalization;

namespace TS4Translator.Core.Packages;

/// <summary>
/// Locale handling for Sims 4 STBL resource keys. The Sims 4 encodes locale in the top byte of an
/// STBL resource's 64-bit instance id; implementations hide that bit-level detail behind standard
/// .NET <see cref="CultureInfo"/> so the rest of the app never touches raw locale bytes.
/// </summary>
public interface IStringTableService
{
    /// <summary>Builds the STBL key for <paramref name="locale"/> with the given group and base instance.</summary>
    /// <exception cref="NotSupportedException"><paramref name="locale"/> has no locale byte supported by The Sims 4.</exception>
    StblKey CreateKey(CultureInfo locale, uint group, ulong instance);

    /// <summary>Returns the equivalent of <paramref name="key"/> for a different locale (same group and base instance).</summary>
    /// <exception cref="NotSupportedException"><paramref name="locale"/> has no locale byte supported by The Sims 4.</exception>
    StblKey WithLocale(StblKey key, CultureInfo locale);

    /// <summary>Reads back the locale encoded in <paramref name="key"/>.</summary>
    /// <exception cref="NotSupportedException">The key's top instance byte is not a locale The Sims 4 supports.</exception>
    CultureInfo GetLocale(StblKey key);
}
