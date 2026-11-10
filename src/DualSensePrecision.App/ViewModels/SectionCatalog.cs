using DualSensePrecision.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DualSensePrecision.App.Views;

public sealed class FeatureItem(string name, FeatureStatus status, string note, string milestone)
{
    public string Name { get; } = name;
    public FeatureStatus Status { get; } = status;
    public string Note { get; } = note;
    public string Milestone { get; } = milestone;

    public string StatusText => Status switch
    {
        FeatureStatus.Complete => "Complete",
        FeatureStatus.InProgress => "In progress",
        FeatureStatus.Blocked => "Blocked",
        FeatureStatus.UnsupportedByHardware => "Unsupported by hardware",
        _ => "Planned",
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[Status switch
    {
        FeatureStatus.Complete => "DsSuccessBrush",
        FeatureStatus.InProgress => "DsAccentBrush",
        FeatureStatus.Blocked => "DsWarningBrush",
        FeatureStatus.UnsupportedByHardware => "DsDangerBrush",
        _ => "DsTextSecondaryBrush",
    }];
}

public sealed record SectionDescriptor(string Title, string Subtitle, string? Notice, IReadOnlyList<FeatureItem> Features);

/// <summary>
/// Truthful status for every section that does not yet have its full UI.
/// "Engine ready" means the processing code exists and is unit-tested; the editor UI is the remaining work.
/// </summary>
public static class SectionCatalog
{
    public static SectionDescriptor Get(string tag) => tag switch
    {
        _ => new("Unknown", "", null, Array.Empty<FeatureItem>()),
    };
}
