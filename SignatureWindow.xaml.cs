using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Photo_zip
{
    public partial class SignatureWindow : Window
    {
        private readonly List<Polyline> _strokes = new List<Polyline>();
        private Polyline _activeStroke;
        private double _brushSize = 4d;

        public SignatureWindow()
        {
            InitializeComponent();
            SetBrushSize(BrushSizeSlider.Value);
        }

        public string SavedSignaturePath { get; private set; }

        public static string SignatureDirectory
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return System.IO.Path.Combine(appData, "Imagility", "Signatures");
            }
        }

        private void SignatureCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BeginStroke(e.GetPosition(SignatureCanvas));
            SignatureCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void SignatureCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeStroke == null || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            AddPoint(e.GetPosition(SignatureCanvas));
            e.Handled = true;
        }

        private void SignatureCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_activeStroke == null)
            {
                return;
            }

            AddPoint(e.GetPosition(SignatureCanvas));
            EndStroke();
            SignatureCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void SignatureCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            BeginStroke(e.GetTouchPoint(SignatureCanvas).Position);
            SignatureCanvas.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void SignatureCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (_activeStroke == null)
            {
                return;
            }

            AddPoint(e.GetTouchPoint(SignatureCanvas).Position);
            e.Handled = true;
        }

        private void SignatureCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            if (_activeStroke == null)
            {
                return;
            }

            AddPoint(e.GetTouchPoint(SignatureCanvas).Position);
            EndStroke();
            SignatureCanvas.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void BeginStroke(Point point)
        {
            _activeStroke = new Polyline
            {
                Stroke = Brushes.Black,
                StrokeThickness = _brushSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            _activeStroke.Points.Add(ClampToCanvas(point));
            SignatureCanvas.Children.Add(_activeStroke);
            SignatureHintText.Visibility = Visibility.Collapsed;
        }

        private void AddPoint(Point point)
        {
            var clamped = ClampToCanvas(point);
            if (_activeStroke.Points.Count > 0)
            {
                var last = _activeStroke.Points[_activeStroke.Points.Count - 1];
                var dx = clamped.X - last.X;
                var dy = clamped.Y - last.Y;
                if (dx * dx + dy * dy < 1d)
                {
                    return;
                }
            }

            _activeStroke.Points.Add(clamped);
        }

        private void EndStroke()
        {
            if (_activeStroke.Points.Count == 1)
            {
                var point = _activeStroke.Points[0];
                _activeStroke.Points.Add(new Point(point.X + 0.1d, point.Y + 0.1d));
            }

            _strokes.Add(_activeStroke);
            _activeStroke = null;
        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SetBrushSize(e.NewValue);
        }

        private void DecreaseBrushSize_Click(object sender, RoutedEventArgs e)
        {
            SetBrushSize(_brushSize - 1d);
        }

        private void IncreaseBrushSize_Click(object sender, RoutedEventArgs e)
        {
            SetBrushSize(_brushSize + 1d);
        }

        private void SetBrushSize(double value)
        {
            _brushSize = Math.Max(2d, Math.Min(10d, Math.Round(value)));

            if (BrushSizeSlider != null && Math.Abs(BrushSizeSlider.Value - _brushSize) > 0.001d)
            {
                BrushSizeSlider.Value = _brushSize;
            }

            if (BrushSizeText != null)
            {
                BrushSizeText.Text = _brushSize.ToString("0", CultureInfo.InvariantCulture) + "px";
            }

            if (_activeStroke != null)
            {
                _activeStroke.StrokeThickness = _brushSize;
            }
        }

        private Point ClampToCanvas(Point point)
        {
            return new Point(
                Math.Max(0, Math.Min(SignatureCanvas.ActualWidth, point.X)),
                Math.Max(0, Math.Min(SignatureCanvas.ActualHeight, point.Y)));
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_activeStroke != null)
            {
                SignatureCanvas.Children.Remove(_activeStroke);
                _activeStroke = null;
                if (_strokes.Count == 0)
                {
                    SignatureHintText.Visibility = Visibility.Visible;
                }

                return;
            }

            if (_strokes.Count == 0)
            {
                return;
            }

            var last = _strokes[_strokes.Count - 1];
            SignatureCanvas.Children.Remove(last);
            _strokes.RemoveAt(_strokes.Count - 1);
            if (_strokes.Count == 0)
            {
                SignatureHintText.Visibility = Visibility.Visible;
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SignatureCanvas.Children.Clear();
            _strokes.Clear();
            _activeStroke = null;
            SignatureHintText.Visibility = Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_strokes.Count == 0)
            {
                MessageBox.Show("请先绘制签名。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Directory.CreateDirectory(SignatureDirectory);
            var path = System.IO.Path.Combine(SignatureDirectory, "signature-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png");
            SaveSignaturePng(path);
            SavedSignaturePath = path;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveSignaturePng(string path)
        {
            var bounds = ResolveStrokeBounds();
            var padding = 12d;
            var left = Math.Max(0d, bounds.Left - padding);
            var top = Math.Max(0d, bounds.Top - padding);
            var right = Math.Min(SignatureCanvas.ActualWidth, bounds.Right + padding);
            var bottom = Math.Min(SignatureCanvas.ActualHeight, bounds.Bottom + padding);
            var width = Math.Max(1, (int)Math.Ceiling(right - left));
            var height = Math.Max(1, (int)Math.Ceiling(bottom - top));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                foreach (var stroke in _strokes)
                {
                    if (stroke.Points.Count == 0)
                    {
                        continue;
                    }

                    var geometry = new StreamGeometry();
                    using (var geometryContext = geometry.Open())
                    {
                        var first = stroke.Points[0];
                        geometryContext.BeginFigure(new Point(first.X - left, first.Y - top), false, false);
                        geometryContext.PolyLineTo(stroke.Points.Skip(1).Select(point => new Point(point.X - left, point.Y - top)).ToArray(), true, true);
                    }

                    geometry.Freeze();
                    var pen = new Pen(Brushes.Black, stroke.StrokeThickness)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    context.DrawGeometry(null, pen, geometry);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
        }

        private Rect ResolveStrokeBounds()
        {
            var points = _strokes.SelectMany(stroke => stroke.Points.Cast<Point>()).ToArray();
            if (points.Length == 0)
            {
                return new Rect(0, 0, 1, 1);
            }

            var maxStroke = _strokes.Count == 0 ? 0d : _strokes.Max(stroke => stroke.StrokeThickness);
            var left = points.Min(point => point.X) - maxStroke / 2d;
            var top = points.Min(point => point.Y) - maxStroke / 2d;
            var right = points.Max(point => point.X) + maxStroke / 2d;
            var bottom = points.Max(point => point.Y) + maxStroke / 2d;
            return new Rect(left, top, Math.Max(1d, right - left), Math.Max(1d, bottom - top));
        }
    }
}