using System.Windows;
using System.Windows.Input;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Interaction logic for DualOverlayWindow.xaml
/// </summary>
public partial class DualOverlayWindow : Window
{
    private readonly DualOverlayViewModel _viewModel;
    private readonly DualOverlayPlacementCoordinator _placementCoordinator;
    private readonly CustomSettingsManager _settingsManager;

    public DualOverlayWindow(
        DualOverlayViewModel viewModel,
        DualOverlayPlacementCoordinator placementCoordinator,
        CustomSettingsManager settingsManager)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _placementCoordinator = placementCoordinator;
        _settingsManager = settingsManager;

        DataContext = _viewModel;

        Loaded += DualOverlayWindow_Loaded;
        _settingsManager.SettingsChanged += SettingsManager_SettingsChanged;
    }

    private void DualOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _placementCoordinator.ApplyPlacement(this);
    }

    private void SettingsManager_SettingsChanged(CustomSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            _placementCoordinator.ApplyPlacement(this);
        });
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Bo qua neu co exception khi nha chuot bat thuong
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Neu dang o che do Floating, luu lai vi tri vua keo tha
        if (_settingsManager.Current.PlacementMode == DualOverlayPlacementMode.Floating)
        {
            _settingsManager.Current.FloatingPositionX = Left;
            _settingsManager.Current.FloatingPositionY = Top;
            _settingsManager.SaveSettings();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _settingsManager.SettingsChanged -= SettingsManager_SettingsChanged;
        base.OnClosed(e);
    }
}
