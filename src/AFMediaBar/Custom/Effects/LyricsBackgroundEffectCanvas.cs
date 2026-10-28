using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Effects;

/// <summary>
/// Canvas vẽ hiệu ứng hạt nền (Particle Background) phía sau lời bài hát (lời gốc / song ngữ).
/// Thiết kế siêu nhẹ (Zero GC Pressure, Frozen Brushes), tự động ngủ đông khi nhạc dừng hoặc ẩn (0% CPU).
/// </summary>
public sealed class LyricsBackgroundEffectCanvas : FrameworkElement
{
    private const int TargetFpsIntervalMs = 33; // ~30 FPS: Cực kỳ mượt mà nhưng tiết kiệm CPU tối đa

    // Cấu hình phạm vi của instance này
    public static readonly DependencyProperty TargetScopeProperty =
        DependencyProperty.Register(
            nameof(TargetScope),
            typeof(LyricsEffectScope),
            typeof(LyricsBackgroundEffectCanvas),
            new PropertyMetadata(LyricsEffectScope.Both, OnTargetScopeChanged));

    public LyricsEffectScope TargetScope
    {
        get => (LyricsEffectScope)GetValue(TargetScopeProperty);
        set => SetValue(TargetScopeProperty, value);
    }

    private static void OnTargetScopeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LyricsBackgroundEffectCanvas canvas)
        {
            canvas.EvaluateRunningState();
        }
    }

    // Trạng thái kích hoạt (Active khi có bài hát đang phát và có lời)
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                EvaluateRunningState();
            }
        }
    }

    // Bộ sinh số ngẫu nhiên nhẹ
    private readonly Random _random = new();

    // Vòng lặp Render 30 FPS
    private readonly DispatcherTimer _renderTimer;

    // Mảng hạt cố định không cấp phát lại (Zero GC allocation)
    private struct Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Size;
        public double Rotation;
        public double RotationSpeed;
        public double Phase;
        public double PhaseSpeed;
        public double BaseAlpha;
        public int ColorIndex;
    }

    private Particle[] _particles = Array.Empty<Particle>();
    private int _particleCount;
    private LyricsBackgroundEffectType _currentEffect = LyricsBackgroundEffectType.None;
    private double _customOpacity = 0.65;
    private double _speedMultiplier = 1.0;
    private double _lastWidth;
    private double _lastHeight;

    // Cọ vẽ đóng băng sẵn (Frozen Brushes) để tối ưu phần cứng WPF tuyệt đối
    private static readonly Brush SakuraBrush1 = CreateFrozenBrush(Color.FromArgb(230, 255, 182, 193)); // Light pink
    private static readonly Brush SakuraBrush2 = CreateFrozenBrush(Color.FromArgb(220, 255, 105, 180)); // Hot pink
    private static readonly Brush SakuraBrush3 = CreateFrozenBrush(Color.FromArgb(240, 255, 192, 203)); // Pastel pink

    private static readonly Brush SnowBrush1 = CreateFrozenBrush(Color.FromArgb(240, 255, 255, 255));
    private static readonly Brush SnowBrush2 = CreateFrozenBrush(Color.FromArgb(180, 224, 242, 254));

    private static readonly Pen RainPen = CreateFrozenPen(Color.FromArgb(190, 160, 216, 239), 1.2);

    private static readonly Brush LeafBrush1 = CreateFrozenBrush(Color.FromArgb(235, 255, 140, 0));   // Dark orange
    private static readonly Brush LeafBrush2 = CreateFrozenBrush(Color.FromArgb(230, 230, 81, 0));    // Deep orange
    private static readonly Brush LeafBrush3 = CreateFrozenBrush(Color.FromArgb(220, 218, 165, 32));  // Goldenrod
    private static readonly Brush LeafBrush4 = CreateFrozenBrush(Color.FromArgb(230, 183, 28, 28));   // Red wine

    private static readonly Brush FireflyGlow = CreateFrozenBrush(Color.FromArgb(90, 205, 220, 57));  // Lime glow
    private static readonly Brush FireflyCore = CreateFrozenBrush(Color.FromArgb(250, 255, 255, 141)); // Yellow core

    private static readonly Brush StarBrush1 = CreateFrozenBrush(Color.FromArgb(250, 255, 255, 255));
    private static readonly Brush StarBrush2 = CreateFrozenBrush(Color.FromArgb(220, 224, 231, 255));
    private static readonly Pen StarCrossPen = CreateFrozenPen(Color.FromArgb(140, 255, 255, 255), 0.8);

    private static readonly Pen BubblePen = CreateFrozenPen(Color.FromArgb(160, 178, 235, 242), 1.0);
    private static readonly Brush BubbleHighlight = CreateFrozenBrush(Color.FromArgb(200, 255, 255, 255));

    private static readonly LinearGradientBrush AuroraBrush = CreateFrozenAuroraBrush();

    public LyricsBackgroundEffectCanvas()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(TargetFpsIntervalMs)
        };
        _renderTimer.Tick += OnRenderFrame;

        Loaded += (_, _) =>
        {
            AttachSettings();
            EvaluateRunningState();
        };

        Unloaded += (_, _) =>
        {
            DetachSettings();
            StopTimer();
        };

        IsVisibleChanged += (_, _) => EvaluateRunningState();
        SizeChanged += (_, _) => ReinitializeParticlesIfSizeChanged();
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static LinearGradientBrush CreateFrozenAuroraBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 230, 118), 0.0),
                new GradientStop(Color.FromArgb(70, 0, 229, 255), 0.35),
                new GradientStop(Color.FromArgb(80, 124, 77, 255), 0.70),
                new GradientStop(Color.FromArgb(0, 234, 128, 252), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private void AttachSettings()
    {
        var mgr = CustomSettingsManager.Instance;
        if (mgr != null)
        {
            mgr.SettingsChanged -= OnSettingsChanged;
            mgr.SettingsChanged += OnSettingsChanged;
            ApplySettings(mgr.Current);
        }
    }

    private void DetachSettings()
    {
        var mgr = CustomSettingsManager.Instance;
        if (mgr != null)
        {
            mgr.SettingsChanged -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(CustomSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            ApplySettings(settings);
            EvaluateRunningState();
        });
    }

    private void ApplySettings(CustomSettings settings)
    {
        bool effectChanged = _currentEffect != settings.BackgroundEffect;
        _currentEffect = settings.BackgroundEffect;
        _customOpacity = Math.Clamp(settings.BackgroundEffectOpacity, 0.1, 1.0);

        _speedMultiplier = settings.BackgroundEffectSpeed switch
        {
            LyricsEffectSpeed.Slow => 0.65,
            LyricsEffectSpeed.Fast => 1.5,
            _ => 1.0
        };

        // Quyết định số lượng hạt theo mật độ
        int baseCount = settings.BackgroundEffectDensity switch
        {
            LyricsEffectDensity.Low => 10,
            LyricsEffectDensity.High => 32,
            _ => 20
        };

        if (_currentEffect == LyricsBackgroundEffectType.Rain)
        {
            baseCount = (int)(baseCount * 1.8);
        }
        else if (_currentEffect == LyricsBackgroundEffectType.AuroraWave)
        {
            baseCount = 1;
        }

        if (effectChanged || _particleCount != baseCount)
        {
            _particleCount = baseCount;
            InitParticles();
        }
    }

    private bool ShouldRun()
    {
        if (!_isActive || !IsVisible || _currentEffect == LyricsBackgroundEffectType.None)
        {
            return false;
        }

        var mgr = CustomSettingsManager.Instance;
        if (mgr == null) return false;

        var scope = mgr.Current.BackgroundEffectScope;
        if (scope == LyricsEffectScope.Both) return true;

        if (TargetScope == LyricsEffectScope.Both) return true;

        return TargetScope == scope;
    }

    private void EvaluateRunningState()
    {
        if (ShouldRun() && ActualWidth > 0 && ActualHeight > 0)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void StartTimer()
    {
        if (!_renderTimer.IsEnabled)
        {
            _renderTimer.Start();
            InvalidateVisual();
        }
    }

    private void StopTimer()
    {
        if (_renderTimer.IsEnabled)
        {
            _renderTimer.Stop();
            InvalidateVisual();
        }
    }

    private void ReinitializeParticlesIfSizeChanged()
    {
        if (Math.Abs(ActualWidth - _lastWidth) > 10 || Math.Abs(ActualHeight - _lastHeight) > 10)
        {
            _lastWidth = ActualWidth;
            _lastHeight = ActualHeight;
            InitParticles();
            EvaluateRunningState();
        }
    }

    private void InitParticles()
    {
        double w = ActualWidth > 10 ? ActualWidth : 400;
        double h = ActualHeight > 5 ? ActualHeight : 42;

        if (_particles.Length != _particleCount)
        {
            _particles = new Particle[_particleCount];
        }

        for (int i = 0; i < _particleCount; i++)
        {
            ResetParticle(ref _particles[i], w, h, randomizeY: true);
        }
    }

    private void ResetParticle(ref Particle p, double w, double h, bool randomizeY)
    {
        p.X = _random.NextDouble() * w;
        p.Y = randomizeY ? _random.NextDouble() * h : -10;
        p.Phase = _random.NextDouble() * Math.PI * 2;
        p.PhaseSpeed = 0.03 + _random.NextDouble() * 0.05;
        p.Rotation = _random.NextDouble() * Math.PI * 2;
        p.RotationSpeed = (_random.NextDouble() - 0.5) * 0.06;
        p.BaseAlpha = 0.4 + _random.NextDouble() * 0.6;
        p.ColorIndex = _random.Next(0, 4);

        switch (_currentEffect)
        {
            case LyricsBackgroundEffectType.Sakura:
                p.Size = 4.5 + _random.NextDouble() * 4.5;
                p.Vx = 0.4 + _random.NextDouble() * 0.6;
                p.Vy = (0.5 + _random.NextDouble() * 0.7) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.Snow:
                p.Size = 1.6 + _random.NextDouble() * 2.8;
                p.Vx = (_random.NextDouble() - 0.5) * 0.4;
                p.Vy = (0.4 + _random.NextDouble() * 0.7) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.Rain:
                p.Size = 8.0 + _random.NextDouble() * 7.0;
                p.Vx = 0.8 + _random.NextDouble() * 0.6;
                p.Vy = (5.0 + _random.NextDouble() * 4.0) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.AutumnLeaves:
                p.Size = 5.0 + _random.NextDouble() * 5.0;
                p.Vx = 0.5 + _random.NextDouble() * 0.7;
                p.Vy = (0.6 + _random.NextDouble() * 0.8) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.Fireflies:
                p.Size = 2.0 + _random.NextDouble() * 2.5;
                p.Vx = (_random.NextDouble() - 0.5) * 0.6 * _speedMultiplier;
                p.Vy = (_random.NextDouble() - 0.5) * 0.5 * _speedMultiplier;
                p.Y = randomizeY ? _random.NextDouble() * h : _random.NextDouble() * h;
                break;

            case LyricsBackgroundEffectType.Starfield:
                p.Size = 1.2 + _random.NextDouble() * 2.2;
                p.Vx = 0;
                p.Vy = 0;
                p.Y = _random.NextDouble() * h;
                p.PhaseSpeed = 0.04 + _random.NextDouble() * 0.08;
                break;

            case LyricsBackgroundEffectType.FloatingBubbles:
                p.Size = 3.5 + _random.NextDouble() * 4.5;
                p.Vx = (_random.NextDouble() - 0.5) * 0.3;
                p.Vy = -(0.5 + _random.NextDouble() * 0.7) * _speedMultiplier;
                p.Y = randomizeY ? _random.NextDouble() * h : h + 10;
                break;

            case LyricsBackgroundEffectType.AuroraWave:
                p.Size = h;
                p.Vx = 0.02 * _speedMultiplier;
                p.PhaseSpeed = 0.03 * _speedMultiplier;
                break;
        }
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        if (!ShouldRun() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            StopTimer();
            return;
        }

        UpdateParticles();
        InvalidateVisual();
    }

    private void UpdateParticles()
    {
        double w = ActualWidth;
        double h = ActualHeight;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.Phase += p.PhaseSpeed;
            p.Rotation += p.RotationSpeed;

            switch (_currentEffect)
            {
                case LyricsBackgroundEffectType.Sakura:
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.75;
                    p.Y += p.Vy;
                    if (p.Y > h + 12 || p.X > w + 20)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Snow:
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.45;
                    p.Y += p.Vy;
                    if (p.Y > h + 8 || p.X > w + 10 || p.X < -10)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Rain:
                    p.X += p.Vx;
                    p.Y += p.Vy;
                    if (p.Y > h + 10 || p.X > w + 10)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.AutumnLeaves:
                    p.X += p.Vx + Math.Sin(p.Phase) * 1.1;
                    p.Y += p.Vy;
                    if (p.Y > h + 15 || p.X > w + 20)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Fireflies:
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.4;
                    p.Y += p.Vy + Math.Cos(p.Phase * 0.8) * 0.3;
                    // Bật lại nếu chạm mép
                    if (p.X < 2) { p.X = 2; p.Vx = Math.Abs(p.Vx); }
                    else if (p.X > w - 2) { p.X = w - 2; p.Vx = -Math.Abs(p.Vx); }
                    if (p.Y < 2) { p.Y = 2; p.Vy = Math.Abs(p.Vy); }
                    else if (p.Y > h - 2) { p.Y = h - 2; p.Vy = -Math.Abs(p.Vy); }
                    break;

                case LyricsBackgroundEffectType.Starfield:
                    // Đứng yên, chỉ thay đổi Phase nhấp nháy
                    break;

                case LyricsBackgroundEffectType.FloatingBubbles:
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.5;
                    p.Y += p.Vy;
                    if (p.Y < -12)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.AuroraWave:
                    p.Phase += p.PhaseSpeed;
                    break;
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (!ShouldRun() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double globalOpacity = _customOpacity;

        switch (_currentEffect)
        {
            case LyricsBackgroundEffectType.Sakura:
                RenderSakura(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.Snow:
                RenderSnow(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.Rain:
                RenderRain(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.AutumnLeaves:
                RenderLeaves(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.Fireflies:
                RenderFireflies(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.Starfield:
                RenderStarfield(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.FloatingBubbles:
                RenderBubbles(dc, globalOpacity);
                break;
            case LyricsBackgroundEffectType.AuroraWave:
                RenderAurora(dc, globalOpacity);
                break;
        }
    }

    private void RenderSakura(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            dc.PushOpacity(p.BaseAlpha * opacity);
            dc.PushTransform(new RotateTransform(p.Rotation * 57.3, p.X, p.Y));

            var brush = p.ColorIndex switch
            {
                0 => SakuraBrush1,
                1 => SakuraBrush2,
                _ => SakuraBrush3
            };

            // Vẽ cánh hoa dạng hình hạt đậu/ellipse xoay
            dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size, p.Size * 0.52);

            dc.Pop();
            dc.Pop();
        }
    }

    private void RenderSnow(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            dc.PushOpacity(p.BaseAlpha * opacity);
            var brush = (p.ColorIndex % 2 == 0) ? SnowBrush1 : SnowBrush2;
            dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size, p.Size);
            dc.Pop();
        }
    }

    private void RenderRain(DrawingContext dc, double opacity)
    {
        dc.PushOpacity(opacity);
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            Point start = new Point(p.X, p.Y);
            Point end = new Point(p.X + p.Vx * 2.5, p.Y + p.Size);
            dc.DrawLine(RainPen, start, end);
        }
        dc.Pop();
    }

    private void RenderLeaves(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            dc.PushOpacity(p.BaseAlpha * opacity);
            dc.PushTransform(new RotateTransform(p.Rotation * 57.3, p.X, p.Y));

            var brush = p.ColorIndex switch
            {
                0 => LeafBrush1,
                1 => LeafBrush2,
                2 => LeafBrush3,
                _ => LeafBrush4
            };

            // Vẽ hình lá phong thon
            dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size, p.Size * 0.45);

            dc.Pop();
            dc.Pop();
        }
    }

    private void RenderFireflies(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            // Xung nhịp độ sáng
            double pulse = 0.35 + 0.65 * Math.Abs(Math.Sin(p.Phase));
            dc.PushOpacity(p.BaseAlpha * opacity * pulse);

            // Vòng hào quang ngoài
            dc.DrawEllipse(FireflyGlow, null, new Point(p.X, p.Y), p.Size * 2.2, p.Size * 2.2);
            // Tâm sáng
            dc.DrawEllipse(FireflyCore, null, new Point(p.X, p.Y), p.Size * 0.8, p.Size * 0.8);

            dc.Pop();
        }
    }

    private void RenderStarfield(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            double twinkle = 0.25 + 0.75 * Math.Abs(Math.Sin(p.Phase));
            dc.PushOpacity(p.BaseAlpha * opacity * twinkle);

            var brush = (p.ColorIndex % 2 == 0) ? StarBrush1 : StarBrush2;
            dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size, p.Size);

            // Sao lớn thì có tia lấp lánh chữ thập
            if (p.Size > 2.0 && twinkle > 0.7)
            {
                double arm = p.Size * 2.5;
                dc.DrawLine(StarCrossPen, new Point(p.X - arm, p.Y), new Point(p.X + arm, p.Y));
                dc.DrawLine(StarCrossPen, new Point(p.X, p.Y - arm), new Point(p.X, p.Y + arm));
            }

            dc.Pop();
        }
    }

    private void RenderBubbles(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            dc.PushOpacity(p.BaseAlpha * opacity);

            // Vòng bóng nước
            dc.DrawEllipse(null, BubblePen, new Point(p.X, p.Y), p.Size, p.Size);
            // Đốm phản xạ sáng nhỏ
            dc.DrawEllipse(BubbleHighlight, null, new Point(p.X - p.Size * 0.35, p.Y - p.Size * 0.35), p.Size * 0.25, p.Size * 0.25);

            dc.Pop();
        }
    }

    private void RenderAurora(DrawingContext dc, double opacity)
    {
        if (_particles.Length == 0) return;
        double phase = _particles[0].Phase;
        double w = ActualWidth;
        double h = ActualHeight;

        dc.PushOpacity(opacity * 0.75);

        // Vẽ dải lụa sóng cực quang ở đáy
        var streamGeom = new StreamGeometry();
        using (var ctx = streamGeom.Open())
        {
            ctx.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(0, h * 0.5 + Math.Sin(phase) * 6), isStroked: true, isSmoothJoin: true);

            int segments = 12;
            double step = w / segments;
            for (int s = 1; s <= segments; s++)
            {
                double x = s * step;
                double waveY = h * 0.5 + Math.Sin(phase + s * 0.5) * (h * 0.32) + Math.Cos(phase * 0.7 + s * 0.3) * (h * 0.15);
                ctx.LineTo(new Point(x, waveY), isStroked: true, isSmoothJoin: true);
            }

            ctx.LineTo(new Point(w, h), isStroked: true, isSmoothJoin: true);
        }
        streamGeom.Freeze();

        dc.DrawGeometry(AuroraBrush, null, streamGeom);
        dc.Pop();
    }
}
