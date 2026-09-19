using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MultiPaneExplorer.App.Controls;

/// <summary>列表空白处拖动的橡皮筋选框（半透明强调色矩形，WPF 列表原生不支持框选）。</summary>
public sealed class RubberBandAdorner : Adorner
{
    private readonly Brush _fillBrush;
    private readonly Pen _borderPen;

    public RubberBandAdorner(UIElement adornedElement) : base(adornedElement)
    {
        // 跟随主题强调色（资源缺失时退回中蓝，深浅两版都可辨）
        var accent = System.Windows.Application.Current?.TryFindResource("W11.Accent") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x0A, 0x60, 0xFF));
        var fill = accent.Clone();
        fill.Opacity = 0.18;
        _fillBrush = fill;
        _borderPen = new Pen(accent, 1);
        _borderPen.Freeze();
    }

    private Rect _rect;

    public void Update(Rect rect)
    {
        _rect = rect;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(_fillBrush, _borderPen, _rect);
}
