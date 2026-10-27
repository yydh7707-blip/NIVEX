using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;

namespace NIVEX.Desktop;

public partial class MainWindow : Window
{
    public AppModel Model { get; }

    public MainWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();
        DataContext = model;
        SourceInitialized += (_, _) =>
        {
            var value = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref value, sizeof(int));
        };
        model.PropertyChanged += OnModelChanged;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppModel.HideInput) or null) SyncSecret();
        };
        LoginHost.Opacity = model.ShowShell ? 0 : 1;
        ShellHost.Opacity = model.ShowShell ? 1 : 0;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Model.ReduceMotion) return;
        if (e.PropertyName is nameof(AppModel.ShowShell))
            CrossFade(Model.ShowShell ? ShellHost : LoginHost, Model.ShowShell ? LoginHost : ShellHost);
        if (e.PropertyName is nameof(AppModel.Section))
            PulseSection();
        if (e.PropertyName is nameof(AppModel.LoginError) && !string.IsNullOrWhiteSpace(Model.LoginError))
            Shake(LoginErrorText);
    }

    private void CrossFade(UIElement show, UIElement hide)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        hide.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
    }

    private void PulseSection()
    {
        SectionHost.BeginAnimation(OpacityProperty, null);
        var anim = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        SectionHost.BeginAnimation(OpacityProperty, anim);
    }

    private static void Shake(FrameworkElement target)
    {
        var transform = new System.Windows.Media.TranslateTransform();
        target.RenderTransform = transform;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
    }

    private void Nav(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string section) Model.Section = section;
    }

    private void ToggleMask(object sender, RoutedEventArgs e) => Model.ToggleMask();

    private void OpenCreate(object sender, RoutedEventArgs e) => Model.OpenCommand.Execute(null);

    private void HideChanged(object sender, RoutedEventArgs e) => SyncSecret();

    private void SecretChanged(object sender, RoutedEventArgs e)
    {
        if (!Model.HideInput || DeviceSecret is null) return;
        Model.LoginDeviceId = DeviceSecret.Password;
    }

    private void SyncSecret()
    {
        if (DeviceSecret is null || DeviceBox is null) return;
        if (Model.HideInput)
        {
            DeviceSecret.Password = DeviceBox.Text;
            DeviceSecret.Visibility = Visibility.Visible;
            DeviceBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            DeviceBox.Text = DeviceSecret.Password;
            DeviceSecret.Visibility = Visibility.Collapsed;
            DeviceBox.Visibility = Visibility.Visible;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
