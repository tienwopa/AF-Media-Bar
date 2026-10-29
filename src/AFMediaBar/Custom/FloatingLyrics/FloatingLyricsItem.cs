using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Custom.FloatingLyrics;

/// <summary>
/// Đại diện cho một câu hát trong danh sách Bảng lời bài hát nổi (Floating Lyrics).
/// </summary>
public sealed class FloatingLyricsItem : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isPast;
    private Brush _primaryForeground = Brushes.White;
    private Brush _secondaryForeground = Brushes.LightGray;
    private double _fontSize = 17.0;

    public int Index { get; init; }
    public LyricLine Line { get; init; } = default!;

    public string PrimaryText { get; set; } = string.Empty;
    public string? SecondaryText { get; set; }
    public bool HasSecondary => !string.IsNullOrWhiteSpace(SecondaryText);

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPast
    {
        get => _isPast;
        set
        {
            if (_isPast != value)
            {
                _isPast = value;
                OnPropertyChanged();
            }
        }
    }

    public Brush PrimaryForeground
    {
        get => _primaryForeground;
        set
        {
            if (!ReferenceEquals(_primaryForeground, value))
            {
                _primaryForeground = value;
                OnPropertyChanged();
            }
        }
    }

    public Brush SecondaryForeground
    {
        get => _secondaryForeground;
        set
        {
            if (!ReferenceEquals(_secondaryForeground, value))
            {
                _secondaryForeground = value;
                OnPropertyChanged();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) > 0.1)
            {
                _fontSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondaryFontSize));
            }
        }
    }

    public double SecondaryFontSize => Math.Max(11.0, FontSize * 0.75);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
