using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TS4Translator.Core.Providers;

namespace TS4Translator.App.ViewModels;

/// <summary>One row of the profiles table in Settings.</summary>
public sealed partial class ProviderProfileRowViewModel(ProviderProfile profile) : ObservableObject
{
    public int Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public ProviderKind Kind { get; } = profile.Kind;

    public string KindLabel { get; } = ProviderKindLabels.ToLabel(profile.Kind);

    /// <summary>Model, endpoint and credential state in one line, so the table stays scannable.</summary>
    public string Details { get; } = BuildDetails(profile);

    /// <summary>Which pipeline jobs this profile currently holds, e.g. "Principal · Refino". Empty when it holds none.</summary>
    [ObservableProperty]
    public partial string RolesText { get; set; } = string.Empty;

    /// <summary>Quota line, filled in after a test for the providers that report one (DeepL).</summary>
    [ObservableProperty]
    public partial string UsageText { get; set; } = string.Empty;

    public Visibility UsageVisibility => UsageText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnUsageTextChanged(string value) => OnPropertyChanged(nameof(UsageVisibility));

    /// <summary>
    /// Exposed as a <see cref="Visibility"/> rather than a bool because a <c>DataTemplate</c>'s
    /// <c>x:Bind</c> resolves against this row, not the page — the page's BoolToVisibility helper is
    /// simply not reachable from inside the template.
    /// </summary>
    public Visibility RolesVisibility => RolesText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnRolesTextChanged(string value) => OnPropertyChanged(nameof(RolesVisibility));

    private static string BuildDetails(ProviderProfile profile)
    {
        if (ProviderKindLabels.NeedsNoConfiguration(profile.Kind))
        {
            return "sem configuração necessária";
        }

        var parts = new List<string>(3) { profile.Endpoint };
        if (!string.IsNullOrWhiteSpace(profile.Model))
        {
            parts.Add(profile.Model);
        }

        parts.Add(profile.HasApiKey ? "chave salva" : "sem chave");
        return string.Join(" · ", parts);
    }
}
