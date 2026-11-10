using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class ControllerPage : Page
{
    private UiSampler? _sampler;
    public ControllerViewModel ViewModel { get; } = new();

    public ControllerPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        AppServices.Controller.StatusChanged += OnStatus;
        ViewModel.RefreshIdentity();
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(33));
        _sampler.Tick += ViewModel.RefreshLive;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppServices.Controller.StatusChanged -= OnStatus;
        _sampler?.Stop();
        _sampler = null;
    }

    private void OnStatus(ConnectionStatus _) => DispatcherQueue.TryEnqueue(ViewModel.RefreshIdentity);
}
