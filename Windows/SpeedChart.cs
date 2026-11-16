using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace LLMDuell
{
    /// <summary>Gemeinsame Achsen für beide Diagramme, damit die Kurven direkt vergleichbar sind.</summary>
    struct ChartScale
    {
        public double MaxX;
        public double MaxY;

        public ChartScale(ModelRun a, ModelRun b)
        {
            double peak = 0;
            foreach (SpeedSample sample in a.Samples) peak = Math.Max(peak, sample.Tps);
            foreach (SpeedSample sample in b.Samples) peak = Math.Max(peak, sample.Tps);
            MaxY = Math.Max(10, Math.Max(peak, Math.Max(a.DisplayTps, b.DisplayTps)) * 1.15);
            MaxX = Math.Max(2, Math.Max(LastT(a), LastT(b)));
        }

        static double LastT(ModelRun run) => run.Samples.Count > 0 ? run.Samples[run.Samples.Count - 1].T : 0;
    }

    /// <summary>Verlauf der Tokens pro Sekunde als Fläche mit Linie, dazu der Durchschnitt als gestrichelte Linie.</summary>
    sealed class SpeedChart : FrameworkElement
    {
        static readonly Typeface LabelFont = new Typeface("Segoe UI");

        List<SpeedSample> samples = new List<SpeedSample>();
        ChartScale scale = new ChartScale { MaxX = 2, MaxY = 10 };
        double? average;
        Color color = Colors.Gray;
        Color color2 = Colors.Gray;

        public void SetColors(Color primary, Color secondary)
        {
            color = primary;
            color2 = secondary;
        }

        public void Update(List<SpeedSample> newSamples, ChartScale newScale, double? newAverage)
        {
            samples = newSamples;
            scale = newScale;
            average = newAverage;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double left = 34, right = 6, top = 5, bottom = 18;
            double width = ActualWidth - left - right;
            double height = ActualHeight - top - bottom;
            if (width < 20 || height < 20) return;

            var plot = new Rect(left, top, width, height);
            Brush labelBrush = Theme.Get("Text3");
            var gridPen = new Pen(Theme.Get("GridLine"), 1);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            FormattedText Label(string text) => new FormattedText(
                text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFont, 10.5, labelBrush, dip);

            double X(double t) => plot.Left + t / scale.MaxX * plot.Width;
            double Y(double tps) => plot.Bottom - tps / scale.MaxY * plot.Height;
            double Snap(double v) => Math.Round(v) + 0.5;

            // Waagerechte Linien mit Beschriftung
            double yStep = NiceStep(scale.MaxY / 3);
            for (double value = 0; value <= scale.MaxY + 1e-9; value += yStep)
            {
                double y = Snap(Y(value));
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                FormattedText text = Label(value.ToString("0.#", CultureInfo.CurrentCulture));
                dc.DrawText(text, new Point(plot.Left - 6 - text.Width, y - text.Height / 2));
            }

            // Senkrechte Linien; kein Label direkt am rechten Rand, dort würde es abgeschnitten.
            double xStep = TickStride(scale.MaxX);
            for (double t = 0; t < scale.MaxX * 0.95; t += xStep)
            {
                double x = Snap(X(t));
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                FormattedText text = Label((int)t + " s");
                dc.DrawText(text, new Point(Math.Max(plot.Left, x - text.Width / 2), plot.Bottom + 3));
            }

            if (samples.Count == 0)
            {
                FormattedText text = Label("Noch keine Daten");
                dc.DrawText(text, new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Top + (plot.Height - text.Height) / 2));
                return;
            }

            dc.PushClip(new RectangleGeometry(plot));

            List<Point> points = samples.Select(s => new Point(X(s.T), Y(s.Tps))).ToList();
            if (points.Count == 1)
            {
                dc.DrawEllipse(new SolidColorBrush(color), null, points[0], 2.5, 2.5);
            }
            else
            {
                var fill = new LinearGradientBrush(WithAlpha(color, 0.35), WithAlpha(color, 0.02), 90);
                dc.DrawGeometry(fill, null, Curve(points, plot.Bottom));

                var stroke = new Pen(new LinearGradientBrush(color, color2, 0), 2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                dc.DrawGeometry(null, stroke, Curve(points, null));
            }

            if (average != null)
            {
                var pen = new Pen(new SolidColorBrush(WithAlpha(color, 0.7)), 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
                double y = Snap(Y(average.Value));
                dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            dc.Pop();
        }

        /// <summary>Glatte Kurve durch alle Punkte (Catmull-Rom). Mit <paramref name="baseline"/> wird sie zur Fläche geschlossen.</summary>
        static Geometry Curve(List<Point> p, double? baseline)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                bool filled = baseline != null;
                if (filled)
                {
                    ctx.BeginFigure(new Point(p[0].X, baseline.Value), true, true);
                    ctx.LineTo(p[0], false, false);
                }
                else
                {
                    ctx.BeginFigure(p[0], false, false);
                }

                for (int i = 0; i < p.Count - 1; i++)
                {
                    Point before = p[Math.Max(i - 1, 0)];
                    Point after = p[Math.Min(i + 2, p.Count - 1)];
                    var c1 = new Point(p[i].X + (p[i + 1].X - before.X) / 6, p[i].Y + (p[i + 1].Y - before.Y) / 6);
                    var c2 = new Point(p[i + 1].X - (after.X - p[i].X) / 6, p[i + 1].Y - (after.Y - p[i].Y) / 6);
                    ctx.BezierTo(c1, c2, p[i + 1], !filled, true);
                }

                if (filled) ctx.LineTo(new Point(p[p.Count - 1].X, baseline.Value), false, false);
            }
            geometry.Freeze();
            return geometry;
        }

        static Color WithAlpha(Color c, double alpha) => System.Windows.Media.Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

        static double TickStride(double maxX)
        {
            foreach (double step in new double[] { 1, 2, 5, 10, 15, 30, 60, 120 })
                if (maxX / step <= 7) return step;
            return 300;
        }

        /// <summary>Rundet auf 1, 2 oder 5 mal eine Zehnerpotenz.</summary>
        static double NiceStep(double rough)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;
            double nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
            return nice * magnitude;
        }
    }
}
