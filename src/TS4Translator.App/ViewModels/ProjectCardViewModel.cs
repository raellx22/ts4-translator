using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// A project as the Home list shows it: name, how many mods it covers, and how far its translation has
/// actually got.
/// </summary>
/// <remarks>
/// The list used to show only a name and a raw file count, which said nothing about whether a project
/// was untouched, half translated, or done — the one thing the user opens this screen to find out.
/// </remarks>
public sealed class ProjectCardViewModel
{
    public ProjectCardViewModel(TranslationProject project, ProjectProgress progress)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(progress);

        Id = project.Id;
        Name = project.Name;
        Progress = progress;

        var mods = project.SourcePackagePaths.Count;
        SubtitleText = $"{mods} mod(s) · {project.SourceLocale.Name} → {project.TargetLocale.Name}";

        if (progress.Total == 0)
        {
            ProgressText = "Sem strings importadas";
            PercentComplete = 0;
            return;
        }

        PercentComplete = 100.0 * progress.Done / progress.Total;

        var parts = new List<string>(4) { $"{progress.Done:N0}/{progress.Total:N0} traduzidas ({PercentComplete:F0}%)" };
        if (progress.NeedsReview > 0)
        {
            parts.Add($"{progress.NeedsReview:N0} para revisar");
        }

        if (progress.Pending > 0)
        {
            parts.Add($"{progress.Pending:N0} pendentes");
        }

        ProgressText = string.Join(" · ", parts);
    }

    public Guid Id { get; }

    public string Name { get; }

    public string SubtitleText { get; }

    public string ProgressText { get; }

    public double PercentComplete { get; }

    public ProjectProgress Progress { get; }
}
