using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace NIVEX.Desktop;

public partial class MainWindow : Window
{
    public AppModel Model { get; }

    public MainWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && model.Tools.Confirm.IsOpen)
            {
                model.Tools.Confirm.Dismiss();
                e.Handled = true;
            }
        };
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
        LoginHost.Visibility = model.ShowShell ? Visibility.Collapsed : Visibility.Visible;
        ShellHost.Visibility = model.ShowShell ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 48));
            Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 48));
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
            PlayIntro();
        };
    }

    private bool _introDone;
    private DispatcherTimer? _introTimer;
    private int _transitionVersion;

    private void PlayIntro()
    {
        ApplyColumns();
        if (Model.ReduceMotion)
        {
            FinishIntro();
            return;
        }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var logoFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(720)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = ease };
        IntroMark.BeginAnimation(OpacityProperty, logoFade);
        IntroShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(760)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = ease });
        IntroBlur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(820)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = ease });
        IntroWordmark.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(380), EasingFunction = ease });
        IntroShine.BeginAnimation(WidthProperty, new DoubleAnimation(0, 44, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(520), EasingFunction = ease });
        IntroShine.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.58, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(520), EasingFunction = ease });

        _introTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1320) };
        _introTimer.Tick += (_, _) => FinishIntro();
        _introTimer.Start();
    }

    private void FinishIntro()
    {
        if (_introDone) return;
        _introDone = true;
        _introTimer?.Stop();
        IntroHost.IsHitTestVisible = false;
        if (Model.ReduceMotion)
        {
            IntroHost.Visibility = Visibility.Collapsed;
            BrandPane.Opacity = 1;
            LoginPane.Opacity = 1;
            LoginHost.Visibility = Model.ShowShell ? Visibility.Collapsed : Visibility.Visible;
            ShellHost.Visibility = Model.ShowShell ? Visibility.Visible : Visibility.Collapsed;
            LoginHost.Opacity = 1;
            ShellHost.Opacity = 1;
            return;
        }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var finishFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
        finishFade.Completed += (_, _) => IntroHost.Visibility = Visibility.Collapsed;
        IntroHost.BeginAnimation(OpacityProperty, finishFade);
        if (Model.ShowShell)
        {
            ShellHost.Visibility = Visibility.Visible;
            ShellHost.Opacity = 0;
            ShellHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            LoginHost.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoginHost.Visibility = Visibility.Visible;
            ShellHost.Visibility = Visibility.Collapsed;
            LoginHost.Opacity = 1;
            PlayEntrance();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyColumns();

    private void ApplyColumns()
    {
        var stack = ActualWidth < 920;
        ShellNavColumn.Width = new GridLength(ActualWidth < 1040 ? 176 : 220);
        if (stack)
        {
            Grid.SetColumn(LoginPane, 0);
            Grid.SetRow(LoginPane, 1);
            BrandColumn.Width = new GridLength(1, GridUnitType.Star);
            FormColumn.Width = new GridLength(0);
            BrandRow.Height = GridLength.Auto;
            FormRow.Height = new GridLength(1, GridUnitType.Star);
            ColumnRule.Visibility = Visibility.Collapsed;
        }
        else
        {
            Grid.SetColumn(LoginPane, 1);
            Grid.SetRow(LoginPane, 0);
            BrandColumn.Width = new GridLength(1, GridUnitType.Star);
            FormColumn.Width = new GridLength(0.88, GridUnitType.Star);
            BrandRow.Height = new GridLength(1, GridUnitType.Star);
            FormRow.Height = new GridLength(0);
            ColumnRule.Visibility = Visibility.Visible;
        }
    }

    private void PlayEntrance()
    {
        ApplyColumns();
        if (Model.ReduceMotion || Model.ShowShell)
        {
            BrandPane.Opacity = 1;
            LoginPane.Opacity = 1;
            BrandPane.RenderTransform = null;
            LoginPane.RenderTransform = null;
            return;
        }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var brandShift = new TranslateTransform(-16, 0);
        var formShift = new TranslateTransform(0, 12);
        BrandPane.RenderTransform = brandShift;
        LoginPane.RenderTransform = formShift;
        BrandPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        brandShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        var delay = TimeSpan.FromMilliseconds(60);
        LoginPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = ease });
        formShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = ease });
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.ShowShell) && _introDone)
            CrossFade(Model.ShowShell ? ShellHost : LoginHost, Model.ShowShell ? LoginHost : ShellHost);
        if (e.PropertyName is nameof(AppModel.LoginError))
        {
            var hasError = !string.IsNullOrWhiteSpace(Model.LoginError);
            LoginErrorHost.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            if (hasError && !Model.ReduceMotion)
            {
                LoginErrorHost.Opacity = 0;
                LoginErrorHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
            }
        }
        if (Model.ReduceMotion) return;
        if (e.PropertyName is nameof(AppModel.Section))
            PulseSection();
        if (e.PropertyName is nameof(AppModel.LoginError) && !string.IsNullOrWhiteSpace(Model.LoginError))
            Shake(LoginErrorText);
    }

    private void CrossFade(UIElement show, UIElement hide)
    {
        var transition = ++_transitionVersion;
        var hideOpacity = hide.Opacity;
        show.BeginAnimation(OpacityProperty, null);
        hide.BeginAnimation(OpacityProperty, null);
        hide.Opacity = hideOpacity;
        show.Visibility = Visibility.Visible;
        show.Opacity = 0;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var hideFade = new DoubleAnimation(hide.Opacity, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        hideFade.Completed += (_, _) =>
        {
            if (transition != _transitionVersion) return;
            hide.Visibility = Visibility.Collapsed;
            hide.BeginAnimation(OpacityProperty, null);
            hide.Opacity = 1;
        };
        hide.BeginAnimation(OpacityProperty, hideFade);
        var showFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        showFade.Completed += (_, _) =>
        {
            if (transition != _transitionVersion) return;
            show.BeginAnimation(OpacityProperty, null);
            show.Opacity = 1;
        };
        show.BeginAnimation(OpacityProperty, showFade);
    }

    private void PulseSection() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(RunSectionFlow));

    private void RunSectionFlow()
    {
        SectionHost.BeginAnimation(OpacityProperty, null);
        SectionHost.Opacity = 1;
        var panel = SectionHost.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.IsVisible)
            ?? SectionHost;
        var items = panel is Panel p ? p.Children.OfType<FrameworkElement>().ToList() : new List<FrameworkElement> { panel };
        if (items.Count == 1 && items[0] is UserControl { Content: Panel inner })
            items = inner.Children.OfType<FrameworkElement>().ToList();
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var shift = new TranslateTransform(0, 26);
            item.RenderTransform = shift;
            item.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(Math.Min(i, 10) * 55);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = delay, EasingFunction = ease };
            fade.Completed += (_, _) =>
            {
                item.BeginAnimation(OpacityProperty, null);
                item.Opacity = 1;
            };
            item.BeginAnimation(OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(720))
            {
                BeginTime = delay,
                EasingFunction = ease,
            });
        }
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
