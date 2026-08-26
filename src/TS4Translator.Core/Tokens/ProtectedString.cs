namespace TS4Translator.Core.Tokens;

/// <summary>
/// The result of running <see cref="ITokenProtector.Protect"/> on a source string: the text a
/// translation provider is allowed to see, plus the map needed to restore it afterwards.
/// </summary>
/// <param name="OriginalText">The untouched source string as extracted from the mod.</param>
/// <param name="ProtectedText">The source string with every protected token replaced by an opaque placeholder.</param>
/// <param name="Tokens">The tokens that were extracted, in the order they appeared in <paramref name="OriginalText"/>.</param>
public sealed record ProtectedString(string OriginalText, string ProtectedText, IReadOnlyList<ProtectedToken> Tokens);
