using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NIVEX.Desktop.Views;

public partial class NeverFace : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(FixFaceState),
        typeof(NeverFace),
        new PropertyMetadata(FixFaceState.Idle, OnStateChanged));

    private readonly DispatcherTimer _blinkTimer;
    private readonly Random _random = new();
    private Storyboard? _activeBlink;
    private bool _isLoaded;
    private bool _specialBlinkUsed;
    private bool _blinkInProgress;

    public NeverFace()
    {
        InitializeComponent();
        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            IsEnabled = false,
        };
        _blinkTimer.Tick += OnBlinkTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FixFaceState State
    {
        get => (FixFaceState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is NeverFace face) face.ApplyState();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        ScheduleNextBlink(State is FixFaceState.Analyzing or FixFaceState.Error or FixFaceState.Completed);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        _blinkTimer.Stop();
        StopBlink();
    }

    private void ApplyState()
    {
        if (!_isLoaded) return;
        if (_blinkInProgress && State is FixFaceState.Completed or FixFaceState.Error)
        {
            _specialBlinkUsed = true;
            _blinkTimer.Stop();
            return;
        }
        _blinkTimer.Stop();
        StopBlink();
        _specialBlinkUsed = false;
        if (State == FixFaceState.Analyzing)
        {
            _specialBlinkUsed = true;
            Blink();
            return;
        }
        ScheduleNextBlink(State is FixFaceState.Analyzing or FixFaceState.Error or FixFaceState.Completed);
    }

    private void StopBlink()
    {
        _activeBlink?.Stop(this);
        _activeBlink = null;
        _blinkInProgress = false;
        LeftEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        RightEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        LeftEyeScale.ScaleY = 1;
        RightEyeScale.ScaleY = 1;
    }

    private void ScheduleNextBlink(bool special)
    {
        if (!_isLoaded) return;
        if (special && !_specialBlinkUsed)
        {
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(240);
            _blinkTimer.Start();
            return;
        }

        var delay = State switch
        {
            FixFaceState.Scanning => _random.Next(5200, 8200),
            FixFaceState.Repairing => _random.Next(4600, 7600),
            _ => _random.Next(3000, 5900),
        };
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _blinkTimer.Start();
    }

    private void OnBlinkTimerTick(object? sender, EventArgs args)
    {
        _blinkTimer.Stop();
        if (!_isLoaded) return;
        Blink();
        var special = State is FixFaceState.Analyzing or FixFaceState.Error or FixFaceState.Completed;
        if (special) _specialBlinkUsed = true;
        ScheduleNextBlink(special);
    }

    private void Blink()
    {
        if (_blinkInProgress) return;
        var blink = new Storyboard { FillBehavior = FillBehavior.Stop };
        AddEyeAnimation(blink, LeftEyeScale);
        AddEyeAnimation(blink, RightEyeScale);
        blink.Completed += (_, _) =>
        {
            _activeBlink = null;
            _blinkInProgress = false;
            LeftEyeScale.ScaleY = 1;
            RightEyeScale.ScaleY = 1;
            ScheduleNextBlink(false);
        };
        _activeBlink = blink;
        _blinkInProgress = true;
        blink.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private static void AddEyeAnimation(Storyboard storyboard, System.Windows.Media.ScaleTransform eye)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(72)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(102))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(218)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, eye);
        Storyboard.SetTargetProperty(animation, new PropertyPath(System.Windows.Media.ScaleTransform.ScaleYProperty));
    }
}
