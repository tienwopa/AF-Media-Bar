using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Effects;

/// <summary>
/// Canvas vẽ hiệu ứng hạt nền nghệ thuật cao cấp (High-Realism Particle System) phía sau lời bài hát.
/// Tích hợp vật lý 3D tumbling, khí động học lốc gió, gợn sóng va chạm (splashes), phát quang sinh học
/// và sao băng. Tối ưu phần cứng GPU WPF (Frozen Visuals, 0 GC allocations, 0% CPU khi dừng).
/// </summary>
public sealed class LyricsBackgroundEffectCanvas : FrameworkElement
{
    private const int TargetFpsIntervalMs = 35; // ~28.5 FPS: Tiết kiệm 35% tải CPU/GPU mà vẫn đảm bảo độ mượt mà tự nhiên 

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

    private LyricsBackgroundEffectType? _explicitEffect;
    public LyricsBackgroundEffectType? ExplicitEffect
    {
        get => _explicitEffect;
        set
        {
            if (_explicitEffect != value)
            {
                _explicitEffect = value;
                ApplySettings(CustomSettingsManager.Instance.Current);
                EvaluateRunningState();
            }
        }
    }

    private readonly Random _random = new();
    private readonly DispatcherTimer _renderTimer;
    private bool _isSettingsAttached;

    // Cấu trúc hạt cố định trong bộ nhớ (Zero GC allocations)
    private struct Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Size;
        public double Rotation;
        public double RotationSpeed;
        public double TumbleX;
        public double TumbleSpeedX;
        public double TumbleY;
        public double TumbleSpeedY;
        public double Phase;
        public double PhaseSpeed;
        public double BaseAlpha;
        public int ColorIndex;
        public int TypeVariant; // Biến thể hình dáng (ví dụ: lá phong vs lá thon, tuyết bụi vs hoa tuyết)
    }

    // Cấu trúc giọt nước bắn khi mưa chạm đáy taskbar
    private struct RainSplash
    {
        public double X;
        public double Y;
        public double RadiusX;
        public double RadiusY;
        public double Alpha;
        public double Growth;
        public bool IsActive;
    }

    // Cấu trúc sao băng vụt sáng
    private struct ShootingStar
    {
        public double StartX;
        public double StartY;
        public double CurrentX;
        public double CurrentY;
        public double Length;
        public double Speed;
        public double Alpha;
        public bool IsActive;
        public int CooldownFrames;
    }

    private Particle[] _particles = Array.Empty<Particle>();
    private readonly RainSplash[] _splashes = new RainSplash[16];
    private ShootingStar _shootingStar;

    private int _particleCount;
    private LyricsBackgroundEffectType _currentEffect = LyricsBackgroundEffectType.None;
    private double _customOpacity = 0.65;
    private double _speedMultiplier = 1.0;
    private double _lastWidth;
    private double _lastHeight;

    // ==========================================
    // CỌ VẼ & HÌNH HỌC ĐÓNG BĂNG SẴN (FROZEN DIRECTX GEOMETRIES & BRUSHES)
    // ==========================================

    // 1. Hoa anh đào (Sakura)
    private static readonly Geometry SakuraPetalGeometry = CreateFrozenSakuraGeometry();
    private static readonly Brush SakuraFrontBrush1 = CreateFrozenLinearBrush(Color.FromArgb(245, 255, 182, 193), Color.FromArgb(220, 255, 105, 180));
    private static readonly Brush SakuraFrontBrush2 = CreateFrozenLinearBrush(Color.FromArgb(250, 255, 228, 225), Color.FromArgb(235, 244, 143, 177));
    private static readonly Brush SakuraFrontBrush3 = CreateFrozenLinearBrush(Color.FromArgb(240, 255, 192, 203), Color.FromArgb(230, 233, 30, 99));
    private static readonly Brush SakuraBackBrush = CreateFrozenLinearBrush(Color.FromArgb(240, 216, 27, 96), Color.FromArgb(220, 194, 24, 91));

    // 2. Tuyết rơi & Tinh thể tuyết
    private static readonly Brush SnowCoreBrush = CreateFrozenBrush(Color.FromArgb(255, 255, 255, 255));
    private static readonly RadialGradientBrush SnowSoftBrush = CreateFrozenSnowSoftBrush();
    private static readonly Geometry SnowflakeCrystalGeometry = CreateFrozenSnowflakeCrystalGeometry();
    private static readonly Pen SnowflakePen = CreateFrozenPen(Color.FromArgb(220, 240, 248, 255), 0.9);

    // 3. Mưa & Vệt nước
    private static readonly LinearGradientBrush RainDropBrush = CreateFrozenLinearBrush(Color.FromArgb(80, 144, 202, 249), Color.FromArgb(220, 225, 245, 254));
    private static readonly Pen RainPen = new Pen(RainDropBrush, 1.4);
    private static readonly Pen RainSplashPen = CreateFrozenPen(Color.FromArgb(170, 179, 229, 252), 1.0);

    // 4. Lá thu (Autumn Leaves)
    private static readonly Geometry MapleLeafGeometry = CreateFrozenMapleGeometry();
    private static readonly Geometry BirchLeafGeometry = CreateFrozenBirchGeometry();
    private static readonly Brush MapleRedBrush = CreateFrozenLinearBrush(Color.FromArgb(245, 198, 40, 40), Color.FromArgb(230, 183, 28, 28));
    private static readonly Brush MapleOrangeBrush = CreateFrozenLinearBrush(Color.FromArgb(245, 230, 81, 0), Color.FromArgb(230, 245, 124, 0));
    private static readonly Brush MapleGoldBrush = CreateFrozenLinearBrush(Color.FromArgb(245, 245, 127, 23), Color.FromArgb(230, 255, 179, 0));
    private static readonly Brush MapleRustBrush = CreateFrozenLinearBrush(Color.FromArgb(240, 109, 76, 65), Color.FromArgb(225, 141, 110, 99));

    // 5. Đom đóm (Bioluminescent Fireflies)
    private static readonly RadialGradientBrush FireflyAuraBrush = CreateFrozenFireflyAura();
    private static readonly Brush FireflyHotCore = CreateFrozenBrush(Color.FromArgb(255, 255, 255, 224));

    // 6. Bụi sao tinh tú & Sao băng
    private static readonly Brush StarDiamondBrush = CreateFrozenBrush(Color.FromArgb(255, 255, 255, 255));
    private static readonly Brush StarGoldBrush = CreateFrozenBrush(Color.FromArgb(245, 255, 249, 196));
    private static readonly Brush StarCyanBrush = CreateFrozenBrush(Color.FromArgb(245, 224, 247, 250));
    private static readonly Pen StarSpikePen = CreateFrozenPen(Color.FromArgb(180, 255, 255, 255), 0.7);
    private static readonly LinearGradientBrush ShootingStarBrush = CreateFrozenShootingStarBrush();

    // 7. Bong bóng nước ngũ sắc
    private static readonly LinearGradientBrush BubbleRingBrush = CreateFrozenLinearBrush(Color.FromArgb(190, 128, 222, 234), Color.FromArgb(160, 244, 143, 177));
    private static readonly Pen BubbleRimPen = new Pen(BubbleRingBrush, 1.1);
    private static readonly Brush BubbleHighlightBrush = CreateFrozenBrush(Color.FromArgb(230, 255, 255, 255));

    // 8. Cực quang vũ trụ (Aurora Waves)
    private static readonly LinearGradientBrush AuroraGreenBrush = CreateFrozenAuroraGreen();
    private static readonly LinearGradientBrush AuroraPurpleBrush = CreateFrozenAuroraPurple();

    static LyricsBackgroundEffectCanvas()
    {
        RainPen.Freeze();
        BubbleRimPen.Freeze();
    }

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
            EnsureSettingsAttached();
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

    private void EnsureSettingsAttached()
    {
        if (!_isSettingsAttached)
        {
            CustomSettingsManager.GlobalSettingsChanged -= OnSettingsChanged;
            CustomSettingsManager.GlobalSettingsChanged += OnSettingsChanged;
            _isSettingsAttached = true;
            ApplySettings(CustomSettingsManager.Instance.Current);
        }
    }

    private void DetachSettings()
    {
        if (_isSettingsAttached)
        {
            CustomSettingsManager.GlobalSettingsChanged -= OnSettingsChanged;
            _isSettingsAttached = false;
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
        var targetEffect = _explicitEffect ?? settings.BackgroundEffect;
        bool effectChanged = _currentEffect != targetEffect;
        _currentEffect = targetEffect;
        _customOpacity = Math.Clamp(settings.BackgroundEffectOpacity, 0.1, 1.0);

        if (_currentEffect == LyricsBackgroundEffectType.None)
        {
            _particleCount = 0;
            _particles = Array.Empty<Particle>();
            StopTimer();
            return;
        }

        _speedMultiplier = settings.BackgroundEffectSpeed switch
        {
            LyricsEffectSpeed.Slow => 0.65,
            LyricsEffectSpeed.Fast => 1.45,
            _ => 1.0
        };

        int baseCount = settings.BackgroundEffectDensity switch
        {
            LyricsEffectDensity.Low => 12,
            LyricsEffectDensity.High => 34,
            _ => 22
        };

        if (_currentEffect == LyricsBackgroundEffectType.Rain)
        {
            baseCount = (int)(baseCount * 1.6);
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
        EnsureSettingsAttached();

        if (!_isActive || !IsVisible || _currentEffect == LyricsBackgroundEffectType.None)
        {
            return false;
        }

        if (_explicitEffect.HasValue || TargetScope == LyricsEffectScope.Both)
        {
            return true;
        }

        var mgr = CustomSettingsManager.Instance;
        if (mgr == null) return false;

        var scope = mgr.Current.BackgroundEffectScope;
        if (scope == LyricsEffectScope.Both) return true;

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
        }
        _particles = Array.Empty<Particle>();
        InvalidateVisual();
    }

    private void ReinitializeParticlesIfSizeChanged()
    {
        if (Math.Abs(ActualWidth - _lastWidth) > 8 || Math.Abs(ActualHeight - _lastHeight) > 6)
        {
            _lastWidth = ActualWidth;
            _lastHeight = ActualHeight;
            InitParticles();
            EvaluateRunningState();
        }
    }

    private void InitParticles()
    {
        double w = ActualWidth > 10 ? ActualWidth : 420;
        double h = ActualHeight > 5 ? ActualHeight : 44;

        if (_particles.Length != _particleCount)
        {
            _particles = new Particle[_particleCount];
        }

        for (int i = 0; i < _particleCount; i++)
        {
            ResetParticle(ref _particles[i], w, h, randomizeY: true);
        }

        for (int s = 0; s < _splashes.Length; s++)
        {
            _splashes[s].IsActive = false;
        }

        _shootingStar.IsActive = false;
        _shootingStar.CooldownFrames = _random.Next(120, 260);
    }

    private void ResetParticle(ref Particle p, double w, double h, bool randomizeY)
    {
        p.X = _random.NextDouble() * w;
        p.Y = randomizeY ? _random.NextDouble() * h : -14;
        p.Phase = _random.NextDouble() * Math.PI * 2;
        p.PhaseSpeed = 0.025 + _random.NextDouble() * 0.045;
        p.Rotation = _random.NextDouble() * Math.PI * 2;
        p.RotationSpeed = (_random.NextDouble() - 0.5) * 0.04;
        p.TumbleX = _random.NextDouble() * Math.PI * 2;
        p.TumbleSpeedX = 0.03 + _random.NextDouble() * 0.05;
        p.TumbleY = _random.NextDouble() * Math.PI * 2;
        p.TumbleSpeedY = 0.02 + _random.NextDouble() * 0.03;
        p.BaseAlpha = 0.45 + _random.NextDouble() * 0.55;
        p.ColorIndex = _random.Next(0, 4);
        p.TypeVariant = _random.Next(0, 5);

        switch (_currentEffect)
        {
            case LyricsBackgroundEffectType.Sakura:
                p.Size = 7.0 + _random.NextDouble() * 5.5;
                p.Vx = 0.45 + _random.NextDouble() * 0.65;
                p.Vy = (0.55 + _random.NextDouble() * 0.65) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.Snow:
                // Variant 0: Hoa tuyết tinh thể lớn; Variant 1-2: Bông tuyết cận cảnh; 3-4: Bụi tuyết xa
                if (p.TypeVariant == 0)
                {
                    p.Size = 6.0 + _random.NextDouble() * 3.0;
                    p.Vy = (0.5 + _random.NextDouble() * 0.4) * _speedMultiplier;
                }
                else if (p.TypeVariant <= 2)
                {
                    p.Size = 3.5 + _random.NextDouble() * 2.5;
                    p.Vy = (0.7 + _random.NextDouble() * 0.6) * _speedMultiplier;
                }
                else
                {
                    p.Size = 1.4 + _random.NextDouble() * 1.6;
                    p.Vy = (0.35 + _random.NextDouble() * 0.35) * _speedMultiplier;
                    p.BaseAlpha *= 0.6;
                }
                p.Vx = (_random.NextDouble() - 0.5) * 0.4;
                break;

            case LyricsBackgroundEffectType.Rain:
                p.Size = 13.0 + _random.NextDouble() * 9.0;
                p.Vx = 1.3 + _random.NextDouble() * 0.7;
                p.Vy = (7.5 + _random.NextDouble() * 4.5) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.AutumnLeaves:
                p.Size = 8.5 + _random.NextDouble() * 6.5;
                p.Vx = 0.6 + _random.NextDouble() * 0.8;
                p.Vy = (0.65 + _random.NextDouble() * 0.75) * _speedMultiplier;
                break;

            case LyricsBackgroundEffectType.Fireflies:
                p.Size = 2.8 + _random.NextDouble() * 2.2;
                p.Vx = (_random.NextDouble() - 0.5) * 0.5 * _speedMultiplier;
                p.Vy = (_random.NextDouble() - 0.5) * 0.45 * _speedMultiplier;
                p.Y = _random.NextDouble() * h;
                break;

            case LyricsBackgroundEffectType.Starfield:
                p.Size = 1.4 + _random.NextDouble() * 2.4;
                p.Vx = 0;
                p.Vy = 0;
                p.Y = _random.NextDouble() * h;
                p.PhaseSpeed = 0.03 + _random.NextDouble() * 0.07;
                break;

            case LyricsBackgroundEffectType.FloatingBubbles:
                p.Size = 5.0 + _random.NextDouble() * 6.0;
                p.Vx = (_random.NextDouble() - 0.5) * 0.35;
                p.Vy = -(0.55 + _random.NextDouble() * 0.65) * _speedMultiplier;
                p.Y = randomizeY ? _random.NextDouble() * h : h + 12;
                break;

            case LyricsBackgroundEffectType.AuroraWave:
                p.Size = h;
                p.Vx = 0.02 * _speedMultiplier;
                p.PhaseSpeed = 0.025 * _speedMultiplier;
                break;
        }
    }

    private void SpawnSplash(double x, double y)
    {
        for (int i = 0; i < _splashes.Length; i++)
        {
            if (!_splashes[i].IsActive)
            {
                _splashes[i].X = x;
                _splashes[i].Y = y;
                _splashes[i].RadiusX = 1.5;
                _splashes[i].RadiusY = 0.6;
                _splashes[i].Alpha = 0.75;
                _splashes[i].Growth = 1.4;
                _splashes[i].IsActive = true;
                break;
            }
        }
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        if (!ShouldRun() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            StopTimer();
            return;
        }

        UpdatePhysics();
        InvalidateVisual();
    }

    private void UpdatePhysics()
    {
        double w = ActualWidth;
        double h = ActualHeight;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.Phase += p.PhaseSpeed;
            p.Rotation += p.RotationSpeed;
            p.TumbleX += p.TumbleSpeedX;
            p.TumbleY += p.TumbleSpeedY;

            switch (_currentEffect)
            {
                case LyricsBackgroundEffectType.Sakura:
                    // Khí động học: Khi cánh hoa xoay ngang (mặt đón gió), rơi chậm lại và trôi ngang nhiều hơn
                    double faceDrift = Math.Abs(Math.Cos(p.TumbleX));
                    p.X += p.Vx + Math.Sin(p.Phase) * (0.85 + faceDrift * 0.6);
                    p.Y += p.Vy * (0.7 + (1.0 - faceDrift) * 0.6);
                    if (p.Y > h + 16 || p.X > w + 24)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Snow:
                    p.X += p.Vx + Math.Sin(p.Phase) * Math.Cos(p.Phase * 0.6) * 0.75;
                    p.Y += p.Vy;
                    if (p.Y > h + 10 || p.X > w + 15 || p.X < -15)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Rain:
                    p.X += p.Vx;
                    p.Y += p.Vy;
                    if (p.Y >= h - 2)
                    {
                        SpawnSplash(p.X, h - 1.5);
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    else if (p.X > w + 20)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.AutumnLeaves:
                    // Chuyển động con lắc chao liệng lá thu
                    double rock = Math.Sin(p.Phase);
                    p.X += p.Vx + rock * 1.5;
                    p.Y += p.Vy * (0.8 + Math.Abs(rock) * 0.4);
                    p.Rotation = rock * 0.65;
                    if (p.Y > h + 18 || p.X > w + 24)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.Fireflies:
                    // Chuyển động uốn lượn Brownian mượt mà
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.45 + Math.Cos(p.Phase * 0.5) * 0.25;
                    p.Y += p.Vy + Math.Cos(p.Phase * 0.8) * 0.35;
                    if (p.X < 4) { p.X = 4; p.Vx = Math.Abs(p.Vx); }
                    else if (p.X > w - 4) { p.X = w - 4; p.Vx = -Math.Abs(p.Vx); }
                    if (p.Y < 3) { p.Y = 3; p.Vy = Math.Abs(p.Vy); }
                    else if (p.Y > h - 3) { p.Y = h - 3; p.Vy = -Math.Abs(p.Vy); }
                    break;

                case LyricsBackgroundEffectType.Starfield:
                    // Sao đứng yên, chỉ nhấp nháy nhịp thở
                    break;

                case LyricsBackgroundEffectType.FloatingBubbles:
                    p.X += p.Vx + Math.Sin(p.Phase) * 0.6;
                    p.Y += p.Vy;
                    if (p.Y < -16)
                    {
                        ResetParticle(ref p, w, h, randomizeY: false);
                    }
                    break;

                case LyricsBackgroundEffectType.AuroraWave:
                    p.Phase += p.PhaseSpeed;
                    break;
            }
        }

        // Cập nhật gợn sóng bắn tung toé của giọt mưa
        for (int s = 0; s < _splashes.Length; s++)
        {
            if (_splashes[s].IsActive)
            {
                _splashes[s].RadiusX += _splashes[s].Growth;
                _splashes[s].RadiusY += _splashes[s].Growth * 0.35;
                _splashes[s].Alpha -= 0.12;
                if (_splashes[s].Alpha <= 0 || _splashes[s].RadiusX > 14)
                {
                    _splashes[s].IsActive = false;
                }
            }
        }

        // Cập nhật sao băng
        if (_currentEffect == LyricsBackgroundEffectType.Starfield)
        {
            if (_shootingStar.IsActive)
            {
                _shootingStar.CurrentX += _shootingStar.Speed;
                _shootingStar.CurrentY += _shootingStar.Speed * 0.45;
                _shootingStar.Alpha -= 0.045;
                if (_shootingStar.Alpha <= 0 || _shootingStar.CurrentX > w + 80)
                {
                    _shootingStar.IsActive = false;
                    _shootingStar.CooldownFrames = _random.Next(160, 320);
                }
            }
            else
            {
                _shootingStar.CooldownFrames--;
                if (_shootingStar.CooldownFrames <= 0)
                {
                    _shootingStar.StartX = _random.NextDouble() * (w * 0.5);
                    _shootingStar.StartY = _random.NextDouble() * (h * 0.35);
                    _shootingStar.CurrentX = _shootingStar.StartX;
                    _shootingStar.CurrentY = _shootingStar.StartY;
                    _shootingStar.Length = 45.0 + _random.NextDouble() * 35.0;
                    _shootingStar.Speed = 12.0 + _random.NextDouble() * 8.0;
                    _shootingStar.Alpha = 0.95;
                    _shootingStar.IsActive = true;
                }
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

    // ==========================================
    // CÁC HÀM RENDER NGHỆ THUẬT CHI TIẾT (RENDER PIPELINE)
    // ==========================================

    private void RenderSakura(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            double scaleX = Math.Cos(p.TumbleX);
            double scaleY = Math.Sin(p.TumbleY) * 0.35 + 0.65;
            bool isBack = scaleX < 0;

            dc.PushOpacity(p.BaseAlpha * opacity);

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(Math.Abs(scaleX) * (p.Size / 10.0), scaleY * (p.Size / 10.0)));
            transform.Children.Add(new RotateTransform(p.Rotation * 57.3));
            transform.Children.Add(new TranslateTransform(p.X, p.Y));
            dc.PushTransform(transform);

            var brush = isBack ? SakuraBackBrush : (p.ColorIndex switch
            {
                0 => SakuraFrontBrush1,
                1 => SakuraFrontBrush2,
                _ => SakuraFrontBrush3
            });

            dc.DrawGeometry(brush, null, SakuraPetalGeometry);

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

            if (p.TypeVariant == 0)
            {
                // Hoa tuyết tinh thể 6 cánh lấp lánh xoay nhẹ
                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(p.Size / 10.0, p.Size / 10.0));
                transform.Children.Add(new RotateTransform(p.Rotation * 57.3));
                transform.Children.Add(new TranslateTransform(p.X, p.Y));
                dc.PushTransform(transform);

                dc.DrawGeometry(null, SnowflakePen, SnowflakeCrystalGeometry);
                dc.DrawEllipse(SnowCoreBrush, null, new Point(0, 0), 1.2, 1.2);

                dc.Pop();
            }
            else if (p.TypeVariant <= 2)
            {
                // Bông tuyết lớn có quầng sáng tỏa mềm mại
                dc.DrawEllipse(SnowSoftBrush, null, new Point(p.X, p.Y), p.Size * 1.5, p.Size * 1.5);
                dc.DrawEllipse(SnowCoreBrush, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
            }
            else
            {
                // Hạt tuyết xa
                dc.DrawEllipse(SnowCoreBrush, null, new Point(p.X, p.Y), p.Size, p.Size);
            }

            dc.Pop();
        }
    }

    private void RenderRain(DrawingContext dc, double opacity)
    {
        dc.PushOpacity(opacity);

        // 1. Vẽ các vệt mưa chuyển động nhanh
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            Point start = new Point(p.X, p.Y);
            Point end = new Point(p.X + p.Vx * 2.8, p.Y + p.Size);
            dc.DrawLine(RainPen, start, end);
        }

        // 2. Vẽ các gợn sóng nước va chạm mặt sàn taskbar
        for (int s = 0; s < _splashes.Length; s++)
        {
            if (_splashes[s].IsActive)
            {
                dc.PushOpacity(_splashes[s].Alpha);
                dc.DrawEllipse(null, RainSplashPen, new Point(_splashes[s].X, _splashes[s].Y), _splashes[s].RadiusX, _splashes[s].RadiusY);
                dc.Pop();
            }
        }

        dc.Pop();
    }

    private void RenderLeaves(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            double scaleX = Math.Cos(p.TumbleX);
            bool isMaple = p.TypeVariant % 2 == 0;

            dc.PushOpacity(p.BaseAlpha * opacity);

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(scaleX * (p.Size / 12.0), p.Size / 12.0));
            transform.Children.Add(new RotateTransform(p.Rotation * 57.3));
            transform.Children.Add(new TranslateTransform(p.X, p.Y));
            dc.PushTransform(transform);

            var brush = p.ColorIndex switch
            {
                0 => MapleRedBrush,
                1 => MapleOrangeBrush,
                2 => MapleGoldBrush,
                _ => MapleRustBrush
            };

            dc.DrawGeometry(brush, null, isMaple ? MapleLeafGeometry : BirchLeafGeometry);

            dc.Pop();
            dc.Pop();
        }
    }

    private void RenderFireflies(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            // Nhịp thở phát quang sinh học mềm mại
            double pulse = Math.Pow(Math.Max(0, Math.Sin(p.Phase)), 2.2);
            if (pulse < 0.05) continue;

            dc.PushOpacity(p.BaseAlpha * opacity * pulse);

            // Quầng sáng vàng lục tỏa rộng
            dc.DrawEllipse(FireflyAuraBrush, null, new Point(p.X, p.Y), p.Size * 3.5, p.Size * 3.5);
            // Tâm sáng phát nhiệt
            dc.DrawEllipse(FireflyHotCore, null, new Point(p.X, p.Y), p.Size * 0.7, p.Size * 0.7);

            dc.Pop();
        }
    }

    private void RenderStarfield(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            double twinkle = Math.Pow(Math.Abs(Math.Sin(p.Phase)), 1.8);
            dc.PushOpacity(p.BaseAlpha * opacity * (0.2 + 0.8 * twinkle));

            var brush = p.ColorIndex switch
            {
                0 => StarGoldBrush,
                1 => StarCyanBrush,
                _ => StarDiamondBrush
            };

            dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size * 0.8, p.Size * 0.8);

            // Sao sáng thì có 4 tia tán xạ kim cương
            if (p.Size > 2.0 && twinkle > 0.65)
            {
                double spike = p.Size * (1.8 + twinkle * 2.0);
                dc.DrawLine(StarSpikePen, new Point(p.X - spike, p.Y), new Point(p.X + spike, p.Y));
                dc.DrawLine(StarSpikePen, new Point(p.X, p.Y - spike), new Point(p.X, p.Y + spike));
            }

            dc.Pop();
        }

        // Vẽ sao băng nếu đang vụt sáng
        if (_shootingStar.IsActive)
        {
            dc.PushOpacity(_shootingStar.Alpha * opacity);
            double tailX = _shootingStar.CurrentX - _shootingStar.Length;
            double tailY = _shootingStar.CurrentY - _shootingStar.Length * 0.45;
            var pen = new Pen(ShootingStarBrush, 1.8);
            pen.Freeze();
            dc.DrawLine(pen, new Point(tailX, tailY), new Point(_shootingStar.CurrentX, _shootingStar.CurrentY));
            dc.DrawEllipse(StarDiamondBrush, null, new Point(_shootingStar.CurrentX, _shootingStar.CurrentY), 2.2, 2.2);
            dc.Pop();
        }
    }

    private void RenderBubbles(DrawingContext dc, double opacity)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            // Độ nhấp nhô đàn hồi của bọt nước
            double wobbleX = 1.0 + Math.Sin(p.Phase * 1.5) * 0.12;
            double wobbleY = 1.0 - Math.Sin(p.Phase * 1.5) * 0.12;

            dc.PushOpacity(p.BaseAlpha * opacity);

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(wobbleX, wobbleY));
            transform.Children.Add(new TranslateTransform(p.X, p.Y));
            dc.PushTransform(transform);

            // Vành bong bóng ngũ sắc
            dc.DrawEllipse(null, BubbleRimPen, new Point(0, 0), p.Size, p.Size);
            // Đốm phản chiếu ánh sáng vát ở góc trên bên trái
            dc.DrawEllipse(BubbleHighlightBrush, null, new Point(-p.Size * 0.38, -p.Size * 0.38), p.Size * 0.25, p.Size * 0.18);

            dc.Pop();
            dc.Pop();
        }
    }

    private void RenderAurora(DrawingContext dc, double opacity)
    {
        if (_particles.Length == 0) return;
        double phase = _particles[0].Phase;
        double w = ActualWidth;
        double h = ActualHeight;

        dc.PushOpacity(opacity * 0.85);

        // Lớp 1: Cực quang xanh ngọc
        var stream1 = new StreamGeometry();
        using (var ctx = stream1.Open())
        {
            ctx.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(0, h * 0.4 + Math.Sin(phase) * 6), isStroked: false, isSmoothJoin: true);

            int segments = 16;
            double step = w / segments;
            for (int s = 1; s <= segments; s++)
            {
                double x = s * step;
                double waveY = h * 0.45 + Math.Sin(phase + s * 0.45) * (h * 0.3) + Math.Cos(phase * 0.6 + s * 0.25) * (h * 0.15);
                ctx.LineTo(new Point(x, waveY), isStroked: false, isSmoothJoin: true);
            }
            ctx.LineTo(new Point(w, h), isStroked: false, isSmoothJoin: true);
        }
        stream1.Freeze();
        dc.DrawGeometry(AuroraGreenBrush, null, stream1);

        // Lớp 2: Cực quang tím mộng mơ đan xen
        var stream2 = new StreamGeometry();
        using (var ctx = stream2.Open())
        {
            ctx.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(0, h * 0.5 + Math.Cos(phase * 0.9) * 5), isStroked: false, isSmoothJoin: true);

            int segments = 14;
            double step = w / segments;
            for (int s = 1; s <= segments; s++)
            {
                double x = s * step;
                double waveY = h * 0.52 + Math.Sin(phase * 0.8 + s * 0.5) * (h * 0.25) + Math.Sin(phase * 1.2 + s * 0.3) * (h * 0.12);
                ctx.LineTo(new Point(x, waveY), isStroked: false, isSmoothJoin: true);
            }
            ctx.LineTo(new Point(w, h), isStroked: false, isSmoothJoin: true);
        }
        stream2.Freeze();
        dc.DrawGeometry(AuroraPurpleBrush, null, stream2);

        dc.Pop();
    }

    // ==========================================
    // KHỞI TẠO HÌNH HỌC & CỌ VẼ CHUẨN XÁC
    // ==========================================

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

    private static LinearGradientBrush CreateFrozenLinearBrush(Color c1, Color c2)
    {
        var brush = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Geometry CreateFrozenSakuraGeometry()
    {
        // Cánh hoa anh đào chuẩn với vết khuyết chẻ nhẹ ở đỉnh (Notched Petal)
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            // Cạnh trái phình ra
            ctx.BezierTo(new Point(-4.5, -4), new Point(-5.0, -8), new Point(-2.5, -12), isStroked: false, isSmoothJoin: true);
            // Vết khuyết hình chữ V nhẹ ở đỉnh cánh hoa
            ctx.LineTo(new Point(0, -9.5), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(2.5, -12), isStroked: false, isSmoothJoin: true);
            // Cạnh phải thuôn về cuống
            ctx.BezierTo(new Point(5.0, -8), new Point(4.5, -4), new Point(0, 0), isStroked: false, isSmoothJoin: true);
        }
        geom.Freeze();
        return geom;
    }

    private static Geometry CreateFrozenMapleGeometry()
    {
        // Lá phong 3 thùy mùa thu
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(-3, -3), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(-8, -4), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(-4, -7), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(-6, -11), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(-2, -9), isStroked: false, isSmoothJoin: true);
            // Đỉnh giữa
            ctx.LineTo(new Point(0, -14), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(2, -9), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(6, -11), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(4, -7), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(8, -4), isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(3, -3), isStroked: false, isSmoothJoin: true);
        }
        geom.Freeze();
        return geom;
    }

    private static Geometry CreateFrozenBirchGeometry()
    {
        // Chiếc lá thu thon nhọn có cuống
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(-5, -4), new Point(-5, -10), new Point(0, -14), isStroked: false, isSmoothJoin: true);
            ctx.BezierTo(new Point(5, -10), new Point(5, -4), new Point(0, 0), isStroked: false, isSmoothJoin: true);
        }
        geom.Freeze();
        return geom;
    }

    private static Geometry CreateFrozenSnowflakeCrystalGeometry()
    {
        // Tinh thể hoa tuyết 6 cánh đối xứng
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            for (int i = 0; i < 6; i++)
            {
                double angle = i * Math.PI / 3.0;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                ctx.BeginFigure(new Point(0, 0), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(cos * 10, sin * 10), isStroked: true, isSmoothJoin: true);

                // Nhánh phụ chữ V trên mỗi cánh
                double branchX = cos * 6;
                double branchY = sin * 6;
                double perpX = -sin * 3;
                double perpY = cos * 3;

                ctx.BeginFigure(new Point(branchX + perpX, branchY + perpY), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(branchX, branchY), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(branchX - perpX, branchY - perpY), isStroked: true, isSmoothJoin: true);
            }
        }
        geom.Freeze();
        return geom;
    }

    private static RadialGradientBrush CreateFrozenSnowSoftBrush()
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(230, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(120, 240, 248, 255), 0.5),
                new GradientStop(Color.FromArgb(0, 224, 242, 254), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush CreateFrozenFireflyAura()
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(240, 255, 255, 140), 0.0),
                new GradientStop(Color.FromArgb(120, 205, 220, 57), 0.45),
                new GradientStop(Color.FromArgb(0, 139, 195, 74), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenShootingStarBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0.45),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(120, 187, 222, 251), 0.6),
                new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenAuroraGreen()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 230, 118), 0.0),
                new GradientStop(Color.FromArgb(90, 0, 229, 255), 0.4),
                new GradientStop(Color.FromArgb(0, 100, 255, 218), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenAuroraPurple()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 124, 77, 255), 0.0),
                new GradientStop(Color.FromArgb(80, 213, 0, 249), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 64, 129), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }
}
