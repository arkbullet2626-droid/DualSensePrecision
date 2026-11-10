using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class FeatureStatusPage : Page
{
    public FeatureStatusPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SectionDescriptor d) return;
        TitleText.Text = d.Title;
        SubtitleText.Text = d.Subtitle;
        Notice.Message = d.Notice ?? "";
        Notice.IsOpen = d.Notice != null;
        FeatureList.ItemsSource = d.Features;
    }
}
