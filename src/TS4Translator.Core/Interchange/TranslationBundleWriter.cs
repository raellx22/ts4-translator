using System.Globalization;
using System.Text;

namespace TS4Translator.Core.Interchange;

/// <summary>
/// Renders a <see cref="TranslationBundle"/> as the plain-text file the user pastes into a chat model.
/// </summary>
/// <remarks>
/// Every choice here exists to survive a round trip through a chat window:
/// <list type="bullet">
/// <item>One line per field, with <c>\n</c> escapes, so a model that reflows text cannot break a record
/// across an unpredictable number of lines.</item>
/// <item>The id on its own line, before the text, so it is never mistaken for content to translate.</item>
/// <item>Explicit start and end markers, so anything the model says before or after the file is easy to
/// discard.</item>
/// <item>Instructions inside the file itself, because the user will paste the file and nothing else.</item>
/// </list>
/// </remarks>
public static class TranslationBundleWriter
{
    public const string FormatHeader = "TS4TRANSLATOR-BUNDLE v1";
    public const string BeginMarker = "=== INICIO ===";
    public const string EndMarker = "=== FIM ===";

    /// <summary>Prefix of the line holding the id of each record.</summary>
    public const string IdPrefix = "#";

    public const string OriginalPrefix = "EN:";
    public const string TranslationPrefix = "PT:";
    public const string DraftPrefix = "ATUAL:";

    public static string Write(TranslationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var builder = new StringBuilder();

        builder.AppendLine(FormatHeader);
        builder.AppendLine(CultureInfo.InvariantCulture, $"projeto: {bundle.ProjectName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"origem: {bundle.SourceLocale.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"destino: {bundle.TargetLocale.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"textos: {bundle.Items.Count}");

        if (bundle.IsSplit)
        {
            // Stated before the instructions and again inside them: a model handed part 3 of 7 otherwise
            // tends to apologise for the "missing" strings, or worse, invent them.
            builder.AppendLine(CultureInfo.InvariantCulture, $"parte: {bundle.PartNumber} de {bundle.PartCount}");
        }

        builder.AppendLine();

        AppendInstructions(builder, bundle);

        builder.AppendLine(BeginMarker);

        foreach (var item in bundle.Items)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"{IdPrefix}{item.Id}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"{OriginalPrefix} {BundleText.Escape(item.Original)}");

            // The existing translation goes on its own line rather than pre-filling PT:, so the parser
            // never has to guess whether a PT: line is the model's answer or the draft echoed back.
            if (!string.IsNullOrWhiteSpace(item.CurrentTranslation))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"{DraftPrefix} {BundleText.Escape(item.CurrentTranslation)}");
            }

            builder.AppendLine(TranslationPrefix);
        }

        builder.AppendLine();
        builder.AppendLine(EndMarker);

        return builder.ToString();
    }

    private static void AppendInstructions(StringBuilder builder, TranslationBundle bundle)
    {
        var targetLocale = bundle.TargetLocale;

        builder.AppendLine("INSTRUCOES PARA A IA (leia tudo antes de responder):");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"1. Traduza o texto de cada linha \"{OriginalPrefix}\" para {targetLocale.EnglishName} e escreva o resultado");
        builder.AppendLine(CultureInfo.InvariantCulture, $"   na linha \"{TranslationPrefix}\" do mesmo bloco.");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"2. NAO altere, remova nem renumere as linhas \"{IdPrefix}numero\". Elas identificam cada texto e sem");
        builder.AppendLine("   elas o aplicativo nao consegue reimportar sua resposta.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"3. NAO altere as linhas \"{OriginalPrefix}\". Copie-as como estao.");
        builder.AppendLine("4. Marcadores como {0.SimFirstName}, {M1.he}{F1.she} e <font color='#AB0C10'> sao codigo do");
        builder.AppendLine("   jogo: copie cada um exatamente como aparece, sem traduzir e sem alterar. Voce pode mover");
        builder.AppendLine("   um marcador para a posicao natural da frase no idioma de destino.");
        builder.AppendLine("5. A sequencia \\n representa uma quebra de linha dentro do texto; \\\\ representa uma barra");
        builder.AppendLine("   invertida. Mantenha essas sequencias na traducao.");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"6. A linha \"{DraftPrefix}\", quando existir, e uma traducao automatica ja feita. Leia-a como rascunho e");
        builder.AppendLine("   reescreva o que estiver errado, travado ou pouco natural. Copiar o rascunho sem mudar");
        builder.AppendLine("   nada so faz sentido quando ele ja esta correto - nesse caso, copie mesmo.");
        builder.AppendLine("7. Cada texto e uma frase de interface de jogo, isolada e sem contexto ao redor. Prefira");
        builder.AppendLine("   uma traducao curta e natural, no mesmo registro do original.");
        builder.AppendLine("8. Se um texto nao for traduzivel (um identificador interno, um numero, so marcadores),");
        builder.AppendLine("   repita-o igual na linha \"PT:\". Nao invente traducao e nao deixe o bloco de fora.");

        if (bundle.IsSplit)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"9. Este arquivo e a PARTE {bundle.PartNumber} DE {bundle.PartCount} da traducao deste mod e contem {bundle.Items.Count} textos.");
            builder.AppendLine("   Traduza somente os blocos que estao aqui. Nao procure, nao complete e nao invente os");
            builder.AppendLine("   textos das outras partes - elas serao enviadas separadamente.");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"10. Responda com o arquivo inteiro, de \"{BeginMarker}\" ate \"{EndMarker}\", no mesmo formato, na mesma");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"    ordem e com os {bundle.Items.Count} blocos. Nao adicione comentarios, numeracao propria, tabelas nem");
            builder.AppendLine("    blocos de codigo.");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"9. Responda com o arquivo inteiro, de \"{BeginMarker}\" ate \"{EndMarker}\", no mesmo formato, na mesma");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"   ordem e com os {bundle.Items.Count} blocos. Nao adicione comentarios, numeracao propria, tabelas nem");
            builder.AppendLine("   blocos de codigo.");
        }

        builder.AppendLine();

        AppendGlossary(builder, bundle);
        AppendCustomInstructions(builder, bundle);
    }

    /// <summary>
    /// The terminology the reply has to honour, written as its own block after the rules so a model that
    /// skims the instructions still sees it. Only the terms that occur in this file are listed.
    /// </summary>
    private static void AppendGlossary(StringBuilder builder, TranslationBundle bundle)
    {
        var terms = bundle.RelevantGlossary;
        if (terms.Count == 0)
        {
            return;
        }

        builder.AppendLine("GLOSSARIO OBRIGATORIO (vale mais que a sua propria escolha de palavras):");
        builder.AppendLine();

        // A note only earns a line when it says something about *this* term. The seeded defaults all
        // carry one identical sentence ("Padrão medido: dobrou o acerto exato..."), which used to be
        // printed under every one of hundreds of glossary lines — the same words repeated until they
        // outweighed the terms themselves, telling the model nothing and spending the one budget that
        // matters in a chat window. A note shared by two or more terms in this file is boilerplate by
        // definition, so it is dropped.
        var repeatedNotes = terms
            .Where(term => !string.IsNullOrWhiteSpace(term.Notes))
            .GroupBy(term => term.Notes!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var term in terms)
        {
            var line = term.NeverTranslate
                ? string.Create(CultureInfo.InvariantCulture, $"- \"{term.Original}\" -> NAO TRADUZA. Copie exatamente como esta.")
                : string.Create(CultureInfo.InvariantCulture, $"- \"{term.Original}\" -> \"{term.Translation}\"");
            builder.AppendLine(line);

            if (!string.IsNullOrWhiteSpace(term.Notes) && !repeatedNotes.Contains(term.Notes))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  ({term.Notes})");
            }
        }

        builder.AppendLine();
    }

    private static void AppendCustomInstructions(StringBuilder builder, TranslationBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.CustomInstructions))
        {
            return;
        }

        builder.AppendLine("INSTRUCOES DE ESTILO DESTE PROJETO:");
        builder.AppendLine();
        builder.AppendLine(bundle.CustomInstructions.Trim());
        builder.AppendLine();
    }
}
