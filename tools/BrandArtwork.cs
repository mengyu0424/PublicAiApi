using System.Windows;
using System.Windows.Media;

namespace ApiStudio
{
    public static class BrandArtwork
    {
        public static void Draw(DrawingContext drawing, Rect bounds)
        {
            drawing.PushTransform(new TranslateTransform(bounds.X, bounds.Y));
            drawing.PushTransform(new ScaleTransform(bounds.Width / 64, bounds.Height / 64));
            var fill = new LinearGradientBrush(Color.FromRgb(24,197,172), Color.FromRgb(0,126,134), new Point(0,0), new Point(1,1));
            drawing.DrawRoundedRectangle(fill, null, new Rect(0,0,64,64), 14, 14);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(220,255,250)), 3.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawing.DrawGeometry(null, pen, Geometry.Parse("M21,20 L12,32 L21,44 M43,20 L52,32 L43,44"));
            drawing.DrawGeometry(Brushes.White, null, Geometry.Parse("M35,14 L24,34 L31,34 L28,50 L41,28 L34,28 Z"));
            drawing.Pop(); drawing.Pop();
        }
    }
}