using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.App.Services;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Translation Memory page: browse, correct and delete what the app has learned.
/// </summary>
/// <remarks>
/// The memory decides the translation of every future mod that repeats a line, and until now it had no
/// interface at all — thousands of entries, most of them from a local engine of uneven quality, that
/// could not be read, corrected or removed. A store that silently steers every future result and cannot
/// be inspected is not a feature the user owns.
///
/// Editing writes through the same upsert the automatic pipeline uses, so a corrected entry simply
/// replaces the old one and is credited to "manual".
/// </remarks>
public sealed partial class MemoryPageViewModel(
    ITranslationMemory translationMemory,
    IProjectService projectService,
    IProjectStringService projectStringService,
    IAlignedTranslationHarvester harvester,
    IPathPickerService pathPicker) : ObservableObject
{
    /// <summary>How many entries one page holds. Enough to scan, small enough to stay responsive.</summary>
    private const int PageSize = 100;

    public ObservableCollection<MemoryEntryViewModel> Entries { get; } = [];

    public ObservableCollection<string> ProviderOptions { get; } = [AllProviders];

    private const string AllProviders = "Todas as origens";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedProvider { get; set; } = AllProviders;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanGoToNextPage { get; set; }

    [ObservableProperty]
    public partial bool CanGoToPreviousPage { get; set; }

    private int _pageIndex;
    private int _totalMatches;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await LoadProviderOptionsAsync();
        await SearchAsync();
    }

    private async Task LoadProviderOptionsAsync()
    {
        var counts = await translationMemory.CountByProviderAsync();

        ProviderOptions.Clear();
        ProviderOptions.Add(AllProviders);
        foreach (var count in counts)
        {
            ProviderOptions.Add(count.Provider);
        }

        SelectedProvider = AllProviders;

        SummaryText = counts.Count == 0
            ? "A memória está vazia. Ela é preenchida sozinha conforme você traduz."
            : $"{counts.Sum(count => count.Count):N0} entrada(s) · "
                + string.Join(" · ", counts.Select(count => $"{count.Provider}: {count.Count:N0}"));
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _pageIndex = 0;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        _pageIndex++;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        _pageIndex = Math.Max(0, _pageIndex - 1);
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        try
        {
            IsBusy = true;

            var page = await translationMemory.SearchAsync(
                SearchText,
                SelectedProvider == AllProviders ? null : SelectedProvider,
                _pageIndex * PageSize,
                PageSize);

            _totalMatches = page.TotalMatches;

            Entries.Clear();
            foreach (var entry in page.Entries)
            {
                Entries.Add(new MemoryEntryViewModel(entry));
            }

            var first = _totalMatches == 0 ? 0 : (_pageIndex * PageSize) + 1;
            var last = (_pageIndex * PageSize) + page.Entries.Count;

            StatusText = _totalMatches == 0
                ? "Nenhuma entrada encontrada."
                : $"Mostrando {first:N0}-{last:N0} de {_totalMatches:N0}.";

            CanGoToPreviousPage = _pageIndex > 0;
            CanGoToNextPage = last < _totalMatches;
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao ler a memória ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves a corrected translation over the existing entry, crediting it to the user.</summary>
    public async Task SaveEntryAsync(MemoryEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.HasUnsavedEdit)
        {
            return;
        }

        try
        {
            var text = entry.Translation ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText = "Uma entrada da memória não pode ficar sem tradução. Use \"Excluir\" para removê-la.";
                entry.RevertEdit();
                return;
            }

            await translationMemory.SaveAsync(new TranslationMemoryEntry(
                entry.Original,
                CultureInfo.GetCultureInfo(entry.SourceLocale),
                CultureInfo.GetCultureInfo(entry.TargetLocale),
                text,
                TranslationMemoryPolicy.ManualProvider,
                ProfileName: null,
                DateTimeOffset.UtcNow));

            entry.MarkSaved(TranslationMemoryPolicy.ManualProvider);
            StatusText = "Entrada corrigida. Ela passa a valer para todos os mods daqui em diante.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar ({exception.GetType().Name}): {exception.Message}";
        }
    }

    public async Task DeleteEntryAsync(MemoryEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            await translationMemory.DeleteAsync(
                entry.Original,
                CultureInfo.GetCultureInfo(entry.SourceLocale),
                CultureInfo.GetCultureInfo(entry.TargetLocale));

            Entries.Remove(entry);
            _totalMatches = Math.Max(0, _totalMatches - 1);
            StatusText = "Entrada removida da memória.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao remover ({exception.GetType().Name}): {exception.Message}";
        }
    }

    /// <summary>
    /// Walks every project and records the hand-made translations that were never memorised.
    /// </summary>
    /// <remarks>
    /// A one-off catch-up for work done before the app learned to remember these. Every line the user
    /// edited by hand, and every line brought back from a chat model, sat in its project and nowhere
    /// else — good translations that would have been re-paid for on the next mod. Everything goes
    /// through <see cref="TranslationMemoryPolicy"/>, so an untranslated sentence stored as finished does
    /// not get memorised on the way in.
    ///
    /// Existing memory entries are overwritten by design: where a machine entry and a human one disagree
    /// about the same source text, the human one is the answer.
    /// </remarks>
    [RelayCommand]
    private async Task BackfillAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Varrendo os projetos...";

            var projects = await projectService.ListRecentAsync(int.MaxValue);
            var entries = new List<TranslationMemoryEntry>();
            var skipped = 0;

            foreach (var project in projects)
            {
                foreach (var entry in await projectStringService.GetStringsAsync(project.Id))
                {
                    if (entry.Status is not (ProjectStringStatus.ManuallyEdited or ProjectStringStatus.Refined))
                    {
                        continue;
                    }

                    if (!TranslationMemoryPolicy.ShouldRemember(entry.OriginalText, entry.TranslatedText))
                    {
                        skipped++;
                        continue;
                    }

                    entries.Add(new TranslationMemoryEntry(
                        entry.OriginalText,
                        project.SourceLocale,
                        project.TargetLocale,
                        entry.TranslatedText!,
                        TranslationMemoryPolicy.ManualProvider,
                        ProfileName: null,
                        DateTimeOffset.UtcNow));
                }
            }

            if (entries.Count == 0)
            {
                StatusText = "Nada a importar - as traduções manuais dos seus projetos já estão na memória.";
                return;
            }

            await translationMemory.SaveManyAsync(entries);
            await LoadProviderOptionsAsync();
            await SearchAsync();

            StatusText = $"{entries.Count:N0} tradução(ões) feitas por você importadas para a memória"
                + (skipped > 0 ? $" · {skipped:N0} descartada(s) por serem identificadores ou não terem sido traduzidas." : ".");
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao importar dos projetos ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Where the mods live, remembered between the two folder prompts of an import.</summary>
    [ObservableProperty]
    public partial string CommunityImportSummary { get; set; } = string.Empty;

    /// <summary>
    /// Recovers translations that already exist on disk, by pairing installed mods with translation
    /// packages written for them.
    /// </summary>
    /// <remarks>
    /// The single biggest source of translation this app has, and it costs nothing to read. A Sims 4
    /// string keeps the same <c>KeyHash</c> in every locale, so a community translation package and the
    /// mod it was made for line up exactly — no similarity matching, no engine, no guessing.
    ///
    /// Measured on the user's own library: 251 translation packages against 15.111 installed mods gave
    /// <b>19.255 pairs</b>, with placeholders and gendered variants intact because a person translated
    /// that exact string. Everything still goes through <see cref="TranslationMemoryPolicy"/>, so an
    /// entry left in English by an unfinished translation does not get memorised on the way in.
    ///
    /// Credited to "comunidade" rather than to a model, so the Memória screen can tell this apart from
    /// machine output and clear it in one gesture if the user disagrees with a translator's choices.
    /// </remarks>
    [RelayCommand]
    private async Task ImportCommunityTranslationsAsync()
    {
        try
        {
            CommunityImportSummary = "Escolha a pasta dos mods instalados (de onde vem o texto em inglês)...";
            var modsFolder = await pathPicker.PickFolderAsync();
            if (modsFolder is null)
            {
                CommunityImportSummary = string.Empty;
                return;
            }

            CommunityImportSummary = "Agora a pasta com os pacotes de tradução...";
            var translationsFolder = await pathPicker.PickFolderAsync();
            if (translationsFolder is null)
            {
                CommunityImportSummary = string.Empty;
                return;
            }

            IsBusy = true;
            var progress = new Progress<string>(message => StatusText = message);

            var harvest = await harvester.HarvestAsync(
                [modsFolder], [translationsFolder], DefaultSourceLocale, DefaultTargetLocale, progress);

            var entries = new List<TranslationMemoryEntry>();
            var skipped = 0;
            foreach (var pair in harvest.Pairs)
            {
                if (!TranslationMemoryPolicy.ShouldRemember(pair.OriginalText, pair.TranslatedText))
                {
                    skipped++;
                    continue;
                }

                entries.Add(new TranslationMemoryEntry(
                    pair.OriginalText, DefaultSourceLocale, DefaultTargetLocale, pair.TranslatedText,
                    TranslationMemoryPolicy.CommunityProvider, ProfileName: null, DateTimeOffset.UtcNow));
            }

            await translationMemory.SaveManyAsync(entries);
            await LoadProviderOptionsAsync();
            await SearchAsync();

            CommunityImportSummary =
                $"{harvest.PackagesScanned:N0} pacote(s) lidos · {entries.Count:N0} tradução(ões) importada(s)"
                + (skipped > 0 ? $" · {skipped:N0} descartada(s) por serem identificadores ou não terem sido traduzidas" : string.Empty)
                + (harvest.AmbiguousKeys > 0 ? $" · {harvest.AmbiguousKeys:N0} recusada(s) por ambiguidade entre mods" : string.Empty)
                + ".";
            StatusText = CommunityImportSummary;
        }
        catch (Exception exception)
        {
            CommunityImportSummary = $"Falha ao importar ({exception.GetType().Name}): {exception.Message}";
            StatusText = CommunityImportSummary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static readonly CultureInfo DefaultSourceLocale = CultureInfo.GetCultureInfo("en-US");

    private static readonly CultureInfo DefaultTargetLocale = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Empties everything one engine contributed. The escape hatch for a memory filled by a local model
    /// the user has since stopped trusting — without it, the only remedy is deleting entries one at a
    /// time, which for eight thousand rows is no remedy at all.
    /// </summary>
    public async Task DeleteProviderAsync(string provider)
    {
        try
        {
            IsBusy = true;

            var removed = await translationMemory.DeleteByProviderAsync(provider);
            await LoadProviderOptionsAsync();
            await SearchAsync();

            StatusText = $"{removed:N0} entrada(s) de \"{provider}\" removida(s).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao limpar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One row of the translation-memory list, editable in place.</summary>
public sealed partial class MemoryEntryViewModel(TranslationMemoryEntry entry) : ObservableObject
{
    public string Original { get; } = entry.OriginalText;

    public string SourceLocale { get; } = entry.SourceLocale.Name;

    public string TargetLocale { get; } = entry.TargetLocale.Name;

    [ObservableProperty]
    public partial string? Translation { get; set; } = entry.TranslatedText;

    [ObservableProperty]
    public partial string Origin { get; set; } = Describe(entry.Provider, entry.CreatedAtUtc);

    private string _persisted = entry.TranslatedText;

    public bool HasUnsavedEdit => !string.Equals(Translation ?? string.Empty, _persisted, StringComparison.Ordinal);

    public void MarkSaved(string provider)
    {
        _persisted = Translation ?? string.Empty;
        Origin = Describe(provider, DateTimeOffset.UtcNow);
    }

    /// <summary>Puts back what is stored, for an edit the page refused to accept.</summary>
    public void RevertEdit() => Translation = _persisted;

    private static string Describe(string provider, DateTimeOffset when) =>
        $"{provider} · {when.ToLocalTime():dd/MM/yyyy}";
}
