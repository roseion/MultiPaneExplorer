using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MultiPaneExplorer.App.Controls;

/// <summary>列表空白处拖动的橡皮筋选框（半透明主色矩形，WPF 列表原生不支持框选）。</summary>
public sealed class RubberBandAdorner : Adorner
{
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromArgb(45, 0x00, 0x67, 0xC0));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0)), 1);

    static RubberBandAdorner()
    {
        FillBrush.Freeze();
        BorderPen.Freeze();
    }

    private Rect _rect;

    public RubberBandAdorner(UIElement adornedElement) : base(adornedElement) { }

    public void Update(Rect rect)
    {
        _rect = rect;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(FillBrush, BorderPen, _rect);
}
