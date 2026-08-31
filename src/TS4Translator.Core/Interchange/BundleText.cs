using System.Text;

namespace TS4Translator.Core.Interchange;

/// <summary>
/// Escaping for the one-line-per-field bundle format.
/// </summary>
/// <remarks>
/// Game strings contain real line breaks, and a format where a record can span an unknown number of
/// lines is exactly what a chat model reformats into something unparseable. Collapsing every text onto a
/// single line with <c>\n</c> escapes keeps each record at a fixed shape, which is what makes the round
/// trip through a chat window survivable. The convention is the familiar one from .properties/.po files,
/// so models reproduce it without being taught.
/// </remarks>
public static class BundleText
{
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>. An unknown escape keeps both characters rather than being dropped:
    /// a model that writes a stray backslash must not silently lose a character of the translation.
    /// </summary>
    public static string Unescape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\' || index + 1 >= text.Length)
            {
                builder.Append(text[index]);
                continue;
            }

            index++;
            switch (text[index])
            {
                case '\\': builder.Append('\\'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                default:
                    builder.Append('\\').Append(text[index]);
                    break;
            }
        }

        return builder.ToString();
    }
}
