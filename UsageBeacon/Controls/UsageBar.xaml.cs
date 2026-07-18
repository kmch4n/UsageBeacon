using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace UsageBeacon.Controls;

// 基底クラスは XAML 側で定義済み（System.Windows.Controls.UserControl）
public partial class UsageBar
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(UsageBar),
            new PropertyMetadata(0.0, (d, _) => ((UsageBar)d).UpdateBar()));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public UsageBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateBar();
        Loaded      += (_, _) => UpdateBar();
    }

    private void UpdateBar()
    {
        var clamped = Math.Clamp(Value, 0, 1);
        Fill.Width  = Root.ActualWidth * clamped;
        Fill.Fill   = new SolidColorBrush(clamped < 0.75
            ? MediaColor.FromRgb(0x4C, 0xAF, 0x50)
            : clamped < 0.90
                ? MediaColor.FromRgb(0xFF, 0xC1, 0x07)
                : MediaColor.FromRgb(0xF4, 0x43, 0x36));
    }
}
