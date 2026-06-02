using Microsoft.Win32;
using Photo_zip.Models;
using Photo_zip.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photo_zip
{
    /// <summary>
    /// 主窗口承担界面状态编排：导入文件、刷新预览、批量处理和进度展示。
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly ImageProcessingService _imageService = new ImageProcessingService();
        private CancellationTokenSource _batchCancellationTokenSource;
        private CancellationTokenSource _previewCancellationTokenSource;
        private ImageItem _selectedImage;
        private int _quality = 80;
        private bool _targetSizeEnabled;
        private double _targetSizeKb = 512;
        private string _selectedOutputFormat = "保持原格式";
        private string _selectedCompressionMode = "有损压缩";
        private string _selectedConflictStrategy = "自动重命名";
        private string _outputDirectory;
        private bool _quantizePng;
        private int _pngColorCount = 256;
        private bool _resizeEnabled;
        private double _resizeWidth = 1920;
        private double _resizeHeight = 1080;
        private string _selectedResizeUnit = "像素";
        private int _resizeDpi = 300;
        private bool _keepAspectRatio = true;
        private bool _watermarkEnabled;
        private string _watermarkText = "Imagility";
        private string _selectedWatermarkPosition = "右下角";
        private int _watermarkOpacity = 35;
        private int _watermarkFontSize = 36;
        private bool _backgroundProcessingEnabled;
        private int _backgroundTolerance = 28;
        private int _backgroundFeather = 12;
        private bool _idPhotoProcessingEnabled;
        private string _idPhotoBackgroundColor = "#FFFFFF";
        private string _selectedStitchMode = "网格拼贴";
        private int _stitchColumns = 2;
        private int _stitchSpacing = 0;
        private string _stitchBackgroundColor = "#FFFFFF";
        private int _stitchCanvasWidth = 1600;
        private int _stitchCanvasHeight = 1200;
        private int _stitchPadding = 24;
        private string _selectedCollageFitMode = "填满裁切";
        private string _selectedPdfExportMode = "合并为单个 PDF";
        private int _pdfPageDpi = 96;
        private bool _isRedactionMode;
        private string _selectedRedactionMode = "马赛克";
        private int _mosaicBlockSize = 12;
        private int _blurRadius = 8;
        private int _redactionBrushSize = 32;
        private bool _isApplyingIdPhotoBackground;
        private bool _isDrawingRedaction;
        private readonly List<Point> _currentRedactionPath = new List<Point>();
        private SignatureOverlay _activeSignatureOverlay;
        private Image _activeSignatureImage;
        private Point _signatureDragStartPoint;
        private double _signatureDragStartX;
        private double _signatureDragStartY;
        private int _previewRequestVersion;
        private double _progressValue;
        private bool _isProcessing;
        private bool _isPreviewBusy;
        private bool _isSyncingResizeFromSelectedImage;
        private BitmapImage _editedPreview;
        private string _currentStatus = "就绪";

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            Images = new ObservableCollection<ImageItem>();
            DataContext = this;
            SizeChanged += MainWindow_SizeChanged;
        }

        public ObservableCollection<ImageItem> Images { get; }

        public string[] OutputFormats { get; } = { "保持原格式", "JPEG", "PNG", "WebP", "BMP", "GIF", "TIFF" };
        public string[] CompressionModes { get; } = { "无损压缩", "有损压缩" };
        public string[] ConflictStrategies { get; } = { "覆盖", "自动重命名", "跳过" };
        public string[] WatermarkPositions { get; } = { "左上角", "右上角", "居中", "左下角", "右下角" };
        public string[] ResizeUnits { get; } = { "像素", "厘米", "毫米", "英寸" };
        public string[] StitchModes { get; } = { "纵向拼接", "横向拼接", "网格拼贴", "瀑布流拼贴", "主图海报", "自由排列" };
        public string[] CollageFitModes { get; } = { "填满裁切", "完整留白" };
        public string[] PdfExportModes { get; } = { "合并为单个 PDF", "每张图片输出一个 PDF" };
        public string[] RedactionModes { get; } = { "马赛克", "高斯模糊" };
        public string[] BackgroundColorPalette { get; } = { "#FFFFFF", "#F8FAFC", "#E5E7EB", "#000000", "#EF4444", "#F97316", "#FACC15", "#22C55E", "#3B82F6", "#8B5CF6" };

        public ImageItem SelectedImage
        {
            get { return _selectedImage; }
            set
            {
                if (SetProperty(ref _selectedImage, value))
                {
                    _isDrawingRedaction = false;
                    _activeSignatureOverlay = null;
                    _activeSignatureImage = null;
                    ClearRedactionStrokePreview();
                    RenderSignatureOverlays();
                    IsRedactionMode = false;
                    SyncResizeInputsFromSelectedImage();
                    OnPropertyChanged(nameof(HasSelectedImage));
                    OnPropertyChanged(nameof(CanStart));
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int Quality
        {
            get { return _quality; }
            set
            {
                var clamped = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _quality, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool TargetSizeEnabled
        {
            get { return _targetSizeEnabled; }
            set
            {
                if (SetProperty(ref _targetSizeEnabled, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public double TargetSizeKb
        {
            get { return _targetSizeKb; }
            set
            {
                var clamped = Math.Max(10, Math.Min(512000, value));
                if (SetProperty(ref _targetSizeKb, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedOutputFormat
        {
            get { return _selectedOutputFormat; }
            set
            {
                if (SetProperty(ref _selectedOutputFormat, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedCompressionMode
        {
            get { return _selectedCompressionMode; }
            set
            {
                if (SetProperty(ref _selectedCompressionMode, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedConflictStrategy
        {
            get { return _selectedConflictStrategy; }
            set { SetProperty(ref _selectedConflictStrategy, value); }
        }

        public string OutputDirectory
        {
            get { return _outputDirectory; }
            set { SetProperty(ref _outputDirectory, value); }
        }

        public bool QuantizePng
        {
            get { return _quantizePng; }
            set
            {
                if (SetProperty(ref _quantizePng, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int PngColorCount
        {
            get { return _pngColorCount; }
            set
            {
                var clamped = Math.Max(2, Math.Min(256, value));
                if (SetProperty(ref _pngColorCount, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool ResizeEnabled
        {
            get { return _resizeEnabled; }
            set
            {
                if (SetProperty(ref _resizeEnabled, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public double ResizeWidth
        {
            get { return _resizeWidth; }
            set
            {
                var clamped = Math.Max(0.01, Math.Min(20000, value));
                if (SetProperty(ref _resizeWidth, clamped) && !_isSyncingResizeFromSelectedImage)
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public double ResizeHeight
        {
            get { return _resizeHeight; }
            set
            {
                var clamped = Math.Max(0.01, Math.Min(20000, value));
                if (SetProperty(ref _resizeHeight, clamped) && !_isSyncingResizeFromSelectedImage)
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedResizeUnit
        {
            get { return _selectedResizeUnit; }
            set
            {
                if (SetProperty(ref _selectedResizeUnit, value))
                {
                    SyncResizeInputsFromSelectedImage();
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int ResizeDpi
        {
            get { return _resizeDpi; }
            set
            {
                var clamped = Math.Max(1, Math.Min(2400, value));
                if (SetProperty(ref _resizeDpi, clamped))
                {
                    if (!string.Equals(SelectedResizeUnit, "像素", StringComparison.OrdinalIgnoreCase))
                    {
                        SyncResizeInputsFromSelectedImage();
                    }

                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool KeepAspectRatio
        {
            get { return _keepAspectRatio; }
            set
            {
                if (SetProperty(ref _keepAspectRatio, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool WatermarkEnabled
        {
            get { return _watermarkEnabled; }
            set
            {
                if (SetProperty(ref _watermarkEnabled, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string WatermarkText
        {
            get { return _watermarkText; }
            set
            {
                if (SetProperty(ref _watermarkText, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedWatermarkPosition
        {
            get { return _selectedWatermarkPosition; }
            set
            {
                if (SetProperty(ref _selectedWatermarkPosition, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int WatermarkOpacity
        {
            get { return _watermarkOpacity; }
            set
            {
                var clamped = Math.Max(1, Math.Min(100, value));
                if (SetProperty(ref _watermarkOpacity, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int WatermarkFontSize
        {
            get { return _watermarkFontSize; }
            set
            {
                var clamped = Math.Max(8, Math.Min(240, value));
                if (SetProperty(ref _watermarkFontSize, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool BackgroundProcessingEnabled
        {
            get { return _backgroundProcessingEnabled; }
            set
            {
                if (SetProperty(ref _backgroundProcessingEnabled, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int BackgroundTolerance
        {
            get { return _backgroundTolerance; }
            set
            {
                var clamped = Math.Max(0, Math.Min(255, value));
                if (SetProperty(ref _backgroundTolerance, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int BackgroundFeather
        {
            get { return _backgroundFeather; }
            set
            {
                var clamped = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _backgroundFeather, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public bool IdPhotoProcessingEnabled
        {
            get { return _idPhotoProcessingEnabled; }
            set
            {
                if (SetProperty(ref _idPhotoProcessingEnabled, value) && !_isApplyingIdPhotoBackground)
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string IdPhotoBackgroundColor
        {
            get { return _idPhotoBackgroundColor; }
            set
            {
                if (SetProperty(ref _idPhotoBackgroundColor, value) && !_isApplyingIdPhotoBackground)
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedStitchMode
        {
            get { return _selectedStitchMode; }
            set
            {
                if (SetProperty(ref _selectedStitchMode, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int StitchColumns
        {
            get { return _stitchColumns; }
            set
            {
                var clamped = Math.Max(1, Math.Min(20, value));
                if (SetProperty(ref _stitchColumns, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int StitchSpacing
        {
            get { return _stitchSpacing; }
            set
            {
                var clamped = Math.Max(0, Math.Min(1000, value));
                if (SetProperty(ref _stitchSpacing, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int StitchCanvasWidth
        {
            get { return _stitchCanvasWidth; }
            set
            {
                var clamped = Math.Max(200, Math.Min(20000, value));
                if (SetProperty(ref _stitchCanvasWidth, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int StitchCanvasHeight
        {
            get { return _stitchCanvasHeight; }
            set
            {
                var clamped = Math.Max(200, Math.Min(20000, value));
                if (SetProperty(ref _stitchCanvasHeight, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public int StitchPadding
        {
            get { return _stitchPadding; }
            set
            {
                var clamped = Math.Max(0, Math.Min(2000, value));
                if (SetProperty(ref _stitchPadding, clamped))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedCollageFitMode
        {
            get { return _selectedCollageFitMode; }
            set
            {
                if (SetProperty(ref _selectedCollageFitMode, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string StitchBackgroundColor
        {
            get { return _stitchBackgroundColor; }
            set
            {
                if (SetProperty(ref _stitchBackgroundColor, value))
                {
                    _ = RefreshPreviewAsync();
                }
            }
        }

        public string SelectedPdfExportMode
        {
            get { return _selectedPdfExportMode; }
            set { SetProperty(ref _selectedPdfExportMode, value); }
        }

        public int PdfPageDpi
        {
            get { return _pdfPageDpi; }
            set
            {
                var clamped = Math.Max(72, Math.Min(1200, value));
                SetProperty(ref _pdfPageDpi, clamped);
            }
        }

        public bool IsRedactionMode
        {
            get { return _isRedactionMode; }
            set
            {
                if (SetProperty(ref _isRedactionMode, value))
                {
                    if (!value)
                    {
                        CancelActiveRedactionStroke();
                    }

                    OnPropertyChanged(nameof(RedactionModeButtonText));
                    OnPropertyChanged(nameof(RedactionCursor));
                    if (SignatureOverlayCanvas != null)
                    {
                        SignatureOverlayCanvas.IsHitTestVisible = !value;
                    }
                }
            }
        }

        public string RedactionModeButtonText => IsRedactionMode ? "退出打码" : "打码";

        public Cursor RedactionCursor => IsRedactionMode ? Cursors.Cross : Cursors.Arrow;

        public string SelectedRedactionMode
        {
            get { return _selectedRedactionMode; }
            set { SetProperty(ref _selectedRedactionMode, value); }
        }

        public int MosaicBlockSize
        {
            get { return _mosaicBlockSize; }
            set
            {
                var clamped = Math.Max(2, Math.Min(32, value));
                SetProperty(ref _mosaicBlockSize, clamped);
            }
        }

        public int BlurRadius
        {
            get { return _blurRadius; }
            set
            {
                var clamped = Math.Max(1, Math.Min(20, value));
                SetProperty(ref _blurRadius, clamped);
            }
        }

        public int RedactionBrushSize
        {
            get { return _redactionBrushSize; }
            set
            {
                var clamped = Math.Max(4, Math.Min(120, value));
                SetProperty(ref _redactionBrushSize, clamped);
            }
        }

        public double ProgressValue
        {
            get { return _progressValue; }
            set { SetProperty(ref _progressValue, value); }
        }

        public bool IsProcessing
        {
            get { return _isProcessing; }
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStitch));
                    OnPropertyChanged(nameof(CanExportPdf));
                }
            }
        }

        public bool IsPreviewBusy
        {
            get { return _isPreviewBusy; }
            set { SetProperty(ref _isPreviewBusy, value); }
        }

        public BitmapImage EditedPreview
        {
            get { return _editedPreview; }
            set { SetProperty(ref _editedPreview, value); }
        }

        public string CurrentStatus
        {
            get { return _currentStatus; }
            set { SetProperty(ref _currentStatus, value); }
        }

        public bool HasItems => Images.Count > 0;
        public bool HasSelectedImage => SelectedImage != null;
        public bool HasCheckedImages => Images.Any(x => x.IsSelected);
        public bool CanStart => !IsProcessing && HasSelectedImage;
        public bool CanStitch => !IsProcessing && Images.Count(x => x.IsSelected) >= 2;
        public bool CanExportPdf => !IsProcessing && HasItems;

        private void SyncResizeInputsFromSelectedImage()
        {
            var image = SelectedImage;
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                return;
            }

            double width = image.Width;
            double height = image.Height;
            var dpi = Math.Max(1, ResizeDpi);

            switch (SelectedResizeUnit)
            {
                case "厘米":
                    width = image.Width * 2.54 / dpi;
                    height = image.Height * 2.54 / dpi;
                    break;
                case "毫米":
                    width = image.Width * 25.4 / dpi;
                    height = image.Height * 25.4 / dpi;
                    break;
                case "英寸":
                    width = image.Width / (double)dpi;
                    height = image.Height / (double)dpi;
                    break;
            }

            _isSyncingResizeFromSelectedImage = true;
            try
            {
                ResizeWidth = Math.Round(width, 2);
                ResizeHeight = Math.Round(height, 2);
            }
            finally
            {
                _isSyncingResizeFromSelectedImage = false;
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderSignatureOverlays();
        }

        private async void CreateSignature_Click(object sender, RoutedEventArgs e)
        {
            var path = ShowSignatureWindow();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            CurrentStatus = "签名已保存：" + Path.GetFileName(path);
            if (SelectedImage != null)
            {
                await AddSignatureOverlayAsync(path);
            }
        }

        private async void InsertSignature_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedImage == null)
            {
                MessageBox.Show("请先选择一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var path = SelectSignaturePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await AddSignatureOverlayAsync(path);
        }

        private string ShowSignatureWindow()
        {
            var window = new SignatureWindow
            {
                Owner = this
            };

            return window.ShowDialog() == true ? window.SavedSignaturePath : null;
        }

        private string SelectSignaturePath()
        {
            Directory.CreateDirectory(SignatureWindow.SignatureDirectory);
            var dialog = new OpenFileDialog
            {
                Title = "选择签名 PNG，或取消后使用新建签名",
                Filter = "PNG 签名|*.png",
                InitialDirectory = SignatureWindow.SignatureDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return ShowSignatureWindow();
        }

        private async Task AddSignatureOverlayAsync(string signaturePath)
        {
            if (SelectedImage == null || string.IsNullOrWhiteSpace(signaturePath) || !File.Exists(signaturePath))
            {
                return;
            }

            Size signatureSize;
            try
            {
                signatureSize = GetBitmapPixelSize(signaturePath);
            }
            catch (Exception ex)
            {
                CurrentStatus = "签名不可用：" + ex.Message;
                MessageBox.Show("无法读取所选签名文件。请确认它是有效的 PNG 图片。\n\n" + ex.Message, "签名不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targetWidth = Math.Max(24d, SelectedImage.Width * 0.28d);
            var ratio = signatureSize.Width > 0 && signatureSize.Height > 0
                ? signatureSize.Height / signatureSize.Width
                : 0.35d;
            var targetHeight = Math.Max(12d, targetWidth * ratio);
            var overlay = new SignatureOverlay
            {
                FilePath = signaturePath,
                Width = Math.Min(SelectedImage.Width, targetWidth),
                Height = Math.Min(SelectedImage.Height, targetHeight),
                X = Math.Max(0d, (SelectedImage.Width - targetWidth) / 2d),
                Y = Math.Max(0d, (SelectedImage.Height - targetHeight) / 2d)
            };

            SelectedImage.AddSignatureOverlay(overlay);
            RenderSignatureOverlays();
            CurrentStatus = SelectedImage.SignatureStatusText;
            await RefreshPreviewAsync();
        }

        private static Size GetBitmapPixelSize(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                throw new InvalidOperationException("签名图片尺寸无效。");
            }

            return new Size(bitmap.PixelWidth, bitmap.PixelHeight);
        }

        private async void AddImages_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                await AddPathsAsync(dialog.FileNames);
            }
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                await AddPathsAsync(paths);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async Task AddPathsAsync(string[] paths)
        {
            var files = _imageService.ExpandFiles(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = Images.Select(x => x.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            ImageItem firstAddedItem = null;

            foreach (var file in files)
            {
                if (existing.Contains(file))
                {
                    continue;
                }

                try
                {
                    CurrentStatus = "正在导入 " + Path.GetFileName(file);
                    var item = await _imageService.CreateImageItemAsync(file);
                    item.PropertyChanged += ImageItem_PropertyChanged;
                    Images.Add(item);
                    if (firstAddedItem == null)
                    {
                        firstAddedItem = item;
                    }

                    existing.Add(file);
                    added++;

                    if (string.IsNullOrWhiteSpace(OutputDirectory))
                    {
                        OutputDirectory = Path.Combine(item.DirectoryPath, "Converted");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法导入 {Path.GetFileName(file)}：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            if (firstAddedItem != null)
            {
                SelectedImage = firstAddedItem;
            }
            else if (SelectedImage == null)
            {
                SelectedImage = Images.FirstOrDefault();
            }

            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasCheckedImages));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStitch));
            OnPropertyChanged(nameof(CanExportPdf));
            CurrentStatus = added > 0 ? $"已导入 {added} 张图片" : "没有新增图片";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Images)
            {
                item.IsSelected = true;
            }

            OnPropertyChanged(nameof(HasCheckedImages));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStitch));
            OnPropertyChanged(nameof(CanExportPdf));
        }

        private void UnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Images)
            {
                item.IsSelected = false;
            }

            OnPropertyChanged(nameof(HasCheckedImages));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStitch));
            OnPropertyChanged(nameof(CanExportPdf));
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = Images.Where(x => x.IsSelected).ToList();
            if (checkedItems.Count == 0)
            {
                return;
            }

            var selectedImage = SelectedImage;
            var firstRemovedIndex = checkedItems
                .Select(x => Images.IndexOf(x))
                .Where(x => x >= 0)
                .DefaultIfEmpty(0)
                .Min();

            foreach (var item in checkedItems)
            {
                item.PropertyChanged -= ImageItem_PropertyChanged;
                Images.Remove(item);
            }

            if (Images.Count == 0)
            {
                SelectedImage = null;
                ProgressValue = 0;
                CurrentStatus = $"已移除 {checkedItems.Count} 张选中图片";
            }
            else if (selectedImage != null && Images.Contains(selectedImage))
            {
                SelectedImage = selectedImage;
                CurrentStatus = $"已移除 {checkedItems.Count} 张选中图片";
            }
            else
            {
                SelectedImage = Images[Math.Min(firstRemovedIndex, Images.Count - 1)];
                CurrentStatus = $"已移除 {checkedItems.Count} 张选中图片";
            }

            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasCheckedImages));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStitch));
            OnPropertyChanged(nameof(CanExportPdf));
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Images)
            {
                item.PropertyChanged -= ImageItem_PropertyChanged;
            }

            Images.Clear();
            SelectedImage = null;
            ProgressValue = 0;
            CurrentStatus = "列表已清空";
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasCheckedImages));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStitch));
            OnPropertyChanged(nameof(CanExportPdf));
        }

        private void ImageItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageItem.IsSelected))
            {
                OnPropertyChanged(nameof(HasCheckedImages));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStitch));
                OnPropertyChanged(nameof(CanExportPdf));
                _ = RefreshPreviewAsync();
            }
        }

        private void ChooseOutputDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择输出文件夹",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "选择此文件夹"
            };

            var initialDirectory = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            dialog.InitialDirectory = initialDirectory;

            if (dialog.ShowDialog() == true)
            {
                var selectedDirectory = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(selectedDirectory))
                {
                    OutputDirectory = selectedDirectory;
                }
            }
        }

        private void PickIdPhotoBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string color)
            {
                IdPhotoBackgroundColor = color;
            }
        }

        private void ApplyIdPhotoBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string color)
            {
                _isApplyingIdPhotoBackground = true;
                try
                {
                    IdPhotoProcessingEnabled = true;
                    IdPhotoBackgroundColor = color;
                }
                finally
                {
                    _isApplyingIdPhotoBackground = false;
                }

                CurrentStatus = "已应用证件照底色：" + color;
                _ = RefreshPreviewAsync();
            }
        }

        private void PickStitchBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string color)
            {
                StitchBackgroundColor = color;
            }
        }

        private async void StartConvert_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResolveConversionTargets();
            if (!selected.Any())
            {
                MessageBox.Show("请先选择或勾选需要处理的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ProcessingOptions options;
            try
            {
                options = CreateOptions();
                _imageService.ValidateOptions(options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProgressValue = 0;
            _batchCancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ProcessingProgress>(p =>
            {
                ProgressValue = p.Total <= 0 ? 0 : p.Completed * 100d / p.Total;
                CurrentStatus = $"{p.Message}：{p.CurrentFileName}  ({p.Completed}/{p.Total})";
            });

            try
            {
                await _imageService.ProcessBatchAsync(selected, options, progress, _batchCancellationTokenSource.Token);
                CurrentStatus = selected.Count == 1 ? "处理完成" : "批量处理完成";
                ProgressValue = 100;
            }
            catch (OperationCanceledException)
            {
                CurrentStatus = "已停止转换";
            }
            finally
            {
                IsProcessing = false;
                _batchCancellationTokenSource.Dispose();
                _batchCancellationTokenSource = null;
            }
        }

        private List<ImageItem> ResolveConversionTargets()
        {
            var selected = Images.Where(x => x.IsSelected).ToList();
            if (selected.Any())
            {
                return selected;
            }

            return SelectedImage != null ? new List<ImageItem> { SelectedImage } : new List<ImageItem>();
        }

        private async void StitchImages_Click(object sender, RoutedEventArgs e)
        {
            var selected = Images.Where(x => x.IsSelected).ToList();
            if (selected.Count < 2)
            {
                MessageBox.Show("请至少勾选 2 张图片进行合并/拼接。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ProcessingOptions options;
            StitchOptions stitchOptions;
            try
            {
                options = CreateOptions();
                _imageService.ValidateOptions(options);
                stitchOptions = CreateStitchOptions();
                _imageService.ValidateStitchOptions(stitchOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProgressValue = 0;
            _batchCancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ProcessingProgress>(p =>
            {
                ProgressValue = p.Total <= 0 ? 0 : p.Completed * 100d / p.Total;
                CurrentStatus = $"{p.Message}：{p.CurrentFileName}  ({p.Completed}/{p.Total})";
            });

            try
            {
                var outputPath = await _imageService.StitchImagesAsync(selected, options, stitchOptions, progress, _batchCancellationTokenSource.Token);
                CurrentStatus = "拼接完成：" + Path.GetFileName(outputPath);
                ProgressValue = 100;
            }
            catch (OperationCanceledException)
            {
                CurrentStatus = "已停止拼接";
            }
            catch (Exception ex)
            {
                CurrentStatus = "拼接失败：" + ex.Message;
                MessageBox.Show(ex.Message, "拼接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsProcessing = false;
                _batchCancellationTokenSource.Dispose();
                _batchCancellationTokenSource = null;
            }
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var selected = Images.Where(x => x.IsSelected).ToList();
            if (!selected.Any())
            {
                selected = Images.ToList();
            }

            if (!selected.Any())
            {
                MessageBox.Show("请先导入需要导出 PDF 的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ProcessingOptions options;
            PdfExportOptions pdfOptions;
            try
            {
                options = CreateOptions();
                _imageService.ValidateOptions(options);
                pdfOptions = CreatePdfExportOptions();
                _imageService.ValidatePdfOptions(pdfOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProgressValue = 0;
            _batchCancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ProcessingProgress>(p =>
            {
                ProgressValue = p.Total <= 0 ? 0 : p.Completed * 100d / p.Total;
                CurrentStatus = $"{p.Message}：{p.CurrentFileName}  ({p.Completed}/{p.Total})";
            });

            try
            {
                var outputPaths = await _imageService.ExportPdfAsync(selected, options, pdfOptions, progress, _batchCancellationTokenSource.Token);
                if (outputPaths.Count == 0)
                {
                    ProgressValue = 0;
                    CurrentStatus = "未生成 PDF：全部跳过或失败";
                    MessageBox.Show("未生成任何 PDF。请检查文件名冲突策略或单张图片状态。", "PDF 未生成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ProgressValue = 100;
                CurrentStatus = outputPaths.Count == 1
                    ? "PDF 导出完成：" + Path.GetFileName(outputPaths[0])
                    : $"PDF 导出完成：{outputPaths.Count} 个文件";
            }
            catch (OperationCanceledException)
            {
                CurrentStatus = "已停止 PDF 导出";
            }
            catch (Exception ex)
            {
                CurrentStatus = "PDF 导出失败：" + ex.Message;
                MessageBox.Show(ex.Message, "PDF 导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsProcessing = false;
                _batchCancellationTokenSource.Dispose();
                _batchCancellationTokenSource = null;
            }
        }

        private void StopConvert_Click(object sender, RoutedEventArgs e)
        {
            _batchCancellationTokenSource?.Cancel();
        }

        private void OpenOutputDirectory_Click(object sender, RoutedEventArgs e)
        {
            var directory = OutputDirectory;
            if (string.IsNullOrWhiteSpace(directory) && SelectedImage != null)
            {
                directory = Path.Combine(SelectedImage.DirectoryPath, "Converted");
            }

            if (string.IsNullOrWhiteSpace(directory))
            {
                MessageBox.Show("还没有可打开的输出目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Directory.CreateDirectory(directory);
            Process.Start("explorer.exe", directory);
        }

        private void ToggleRedactionMode_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedImage == null)
            {
                MessageBox.Show("请先选择一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsRedactionMode = !IsRedactionMode;
            CurrentStatus = IsRedactionMode ? "打码模式：按住鼠标在原图上涂改" : "已退出打码模式";
        }

        private async void UndoRedaction_Click(object sender, RoutedEventArgs e)
        {
            await UndoLastRedactionAsync();
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                await UndoLastRedactionAsync();
            }
        }

        private void RedactionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsRedactionMode || SelectedImage == null)
            {
                return;
            }

            _isDrawingRedaction = true;
            ClearRedactionStrokePreview();
            AddRedactionStrokePoint(e.GetPosition(RedactionCanvas), true);
            RedactionCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void RedactionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawingRedaction)
            {
                return;
            }

            AddRedactionStrokePoint(e.GetPosition(RedactionCanvas), false);
            e.Handled = true;
        }

        private async void RedactionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawingRedaction)
            {
                return;
            }

            _isDrawingRedaction = false;
            RedactionCanvas.ReleaseMouseCapture();
            AddRedactionStrokePoint(e.GetPosition(RedactionCanvas), false);

            if (TryCreateRedactionOperation(_currentRedactionPath, out var operation))
            {
                SelectedImage.AddRedaction(operation);
                CurrentStatus = SelectedImage.RedactionStatusText;
                await RefreshPreviewAsync();
            }

            ClearRedactionStrokePreview();
            e.Handled = true;
        }

        private void RedactionCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDrawingRedaction || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                return;
            }

            CancelActiveRedactionStroke();
        }

        private void RedactionCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isDrawingRedaction && Mouse.LeftButton != MouseButtonState.Pressed)
            {
                CancelActiveRedactionStroke();
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            CancelActiveRedactionStroke();
            EndSignatureDrag();
        }

        private async void SignatureOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SelectedImage == null || IsRedactionMode)
            {
                return;
            }

            var hit = e.OriginalSource as FrameworkElement;
            if (!(hit?.Tag is SignatureOverlay overlay))
            {
                return;
            }

            _activeSignatureOverlay = overlay;
            _activeSignatureImage = hit as Image;
            _signatureDragStartPoint = e.GetPosition(RedactionCanvas);
            _signatureDragStartX = overlay.X;
            _signatureDragStartY = overlay.Y;
            SignatureOverlayCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void SignatureOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeSignatureOverlay == null || SelectedImage == null || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var bounds = ResolveDisplayedImageBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var point = e.GetPosition(RedactionCanvas);
            var scaleX = SelectedImage.Width / bounds.Width;
            var scaleY = SelectedImage.Height / bounds.Height;
            _activeSignatureOverlay.X = Math.Max(0d, Math.Min(SelectedImage.Width - _activeSignatureOverlay.Width, _signatureDragStartX + (point.X - _signatureDragStartPoint.X) * scaleX));
            _activeSignatureOverlay.Y = Math.Max(0d, Math.Min(SelectedImage.Height - _activeSignatureOverlay.Height, _signatureDragStartY + (point.Y - _signatureDragStartPoint.Y) * scaleY));
            UpdateSignatureImagePlacement(_activeSignatureImage, _activeSignatureOverlay, bounds);
            e.Handled = true;
        }

        private async void SignatureOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_activeSignatureOverlay == null || SelectedImage == null)
            {
                return;
            }

            PersistSignatureOverlayChanges();
            EndSignatureDrag();
            await RefreshPreviewAsync();
            e.Handled = true;
        }

        private async void SignatureOverlayCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (SelectedImage == null || IsRedactionMode)
            {
                return;
            }

            var hit = e.OriginalSource as FrameworkElement;
            if (!(hit?.Tag is SignatureOverlay overlay))
            {
                return;
            }

            var factor = e.Delta > 0 ? 1.08d : 0.92d;
            var centerX = overlay.X + overlay.Width / 2d;
            var centerY = overlay.Y + overlay.Height / 2d;
            var newWidth = Math.Max(16d, Math.Min(SelectedImage.Width, overlay.Width * factor));
            var newHeight = Math.Max(8d, Math.Min(SelectedImage.Height, overlay.Height * factor));
            overlay.Width = newWidth;
            overlay.Height = newHeight;
            overlay.X = Math.Max(0d, Math.Min(SelectedImage.Width - overlay.Width, centerX - overlay.Width / 2d));
            overlay.Y = Math.Max(0d, Math.Min(SelectedImage.Height - overlay.Height, centerY - overlay.Height / 2d));

            UpdateSignatureImagePlacement(hit as Image, overlay, ResolveDisplayedImageBounds());
            PersistSignatureOverlayChanges();
            await RefreshPreviewAsync();
            e.Handled = true;
        }

        private void EndSignatureDrag()
        {
            if (SignatureOverlayCanvas.IsMouseCaptured)
            {
                SignatureOverlayCanvas.ReleaseMouseCapture();
            }

            _activeSignatureOverlay = null;
            _activeSignatureImage = null;
        }

        private void RenderSignatureOverlays()
        {
            if (SignatureOverlayCanvas == null)
            {
                return;
            }

            SignatureOverlayCanvas.Children.Clear();
            if (SelectedImage == null)
            {
                return;
            }

            var bounds = ResolveDisplayedImageBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            foreach (var overlay in SelectedImage.GetSignatureSnapshot())
            {
                if (string.IsNullOrWhiteSpace(overlay.FilePath) || !File.Exists(overlay.FilePath))
                {
                    continue;
                }

                var image = new Image
                {
                    Source = LoadBitmapImage(overlay.FilePath),
                    Stretch = Stretch.Fill,
                    Tag = overlay,
                    Cursor = Cursors.SizeAll,
                    Opacity = 0.92
                };

                SignatureOverlayCanvas.Children.Add(image);
                UpdateSignatureImagePlacement(image, overlay, bounds);
            }
        }

        private void UpdateSignatureImagePlacement(Image image, SignatureOverlay overlay, Rect bounds)
        {
            if (image == null || overlay == null || SelectedImage == null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var scaleX = bounds.Width / SelectedImage.Width;
            var scaleY = bounds.Height / SelectedImage.Height;
            Canvas.SetLeft(image, bounds.Left + overlay.X * scaleX);
            Canvas.SetTop(image, bounds.Top + overlay.Y * scaleY);
            image.Width = Math.Max(1d, overlay.Width * scaleX);
            image.Height = Math.Max(1d, overlay.Height * scaleY);
        }

        private void PersistSignatureOverlayChanges()
        {
            if (SelectedImage == null)
            {
                return;
            }

            var overlays = SignatureOverlayCanvas.Children
                .OfType<Image>()
                .Select(image => image.Tag as SignatureOverlay)
                .Where(overlay => overlay != null)
                .ToArray();
            SelectedImage.ReplaceSignatureOverlays(overlays);
            CurrentStatus = SelectedImage.SignatureStatusText;
        }

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void CancelActiveRedactionStroke()
        {
            _isDrawingRedaction = false;
            if (RedactionCanvas.IsMouseCaptured)
            {
                RedactionCanvas.ReleaseMouseCapture();
            }

            ClearRedactionStrokePreview();
        }

        private async Task UndoLastRedactionAsync()
        {
            if (SelectedImage == null)
            {
                return;
            }

            if (SelectedImage.UndoLastRedaction())
            {
                CurrentStatus = SelectedImage.RedactionStatusText;
                await RefreshPreviewAsync();
            }
        }

        private void AddRedactionStrokePoint(Point point, bool force)
        {
            var displayBounds = ResolveDisplayedImageBounds();
            if (displayBounds.Width > 0 && displayBounds.Height > 0)
            {
                point = ClampPointToBounds(point, displayBounds);
            }

            if (!force && _currentRedactionPath.Count > 0)
            {
                var last = _currentRedactionPath[_currentRedactionPath.Count - 1];
                var distance = Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2));
                if (distance < 2d)
                {
                    return;
                }
            }

            _currentRedactionPath.Add(point);
            DrawRedactionStrokePreview();
        }

        private void DrawRedactionStrokePreview()
        {
            RedactionStrokePreview.Points.Clear();
            foreach (var point in _currentRedactionPath)
            {
                RedactionStrokePreview.Points.Add(point);
            }

            RedactionStrokePreview.StrokeThickness = RedactionBrushSize;
            RedactionStrokePreview.Visibility = _currentRedactionPath.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearRedactionStrokePreview()
        {
            _currentRedactionPath.Clear();
            RedactionStrokePreview.Points.Clear();
            RedactionStrokePreview.Visibility = Visibility.Collapsed;
        }

        private bool TryCreateRedactionOperation(IReadOnlyList<Point> canvasPoints, out RedactionOperation operation)
        {
            operation = null;
            if (SelectedImage == null || canvasPoints == null || canvasPoints.Count == 0)
            {
                return false;
            }

            var sourceImageWidth = SelectedImage.Width;
            var sourceImageHeight = SelectedImage.Height;
            if (sourceImageWidth <= 0 || sourceImageHeight <= 0)
            {
                return false;
            }

            var displayBounds = ResolveDisplayedImageBounds();
            if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            {
                return false;
            }

            var scaleX = sourceImageWidth / displayBounds.Width;
            var scaleY = sourceImageHeight / displayBounds.Height;
            var brushSize = Math.Max(4, Math.Min(120, RedactionBrushSize));
            var sourceBrushSize = Math.Max(1d, brushSize * Math.Max(scaleX, scaleY));
            var sourcePoints = canvasPoints
                .Select(point => ClampPointToBounds(point, displayBounds))
                .Select(point => new RedactionPoint
                {
                    X = Math.Max(0, Math.Min(sourceImageWidth, (point.X - displayBounds.Left) * scaleX)),
                    Y = Math.Max(0, Math.Min(sourceImageHeight, (point.Y - displayBounds.Top) * scaleY))
                })
                .ToList();

            sourcePoints = DensifyRedactionPath(sourcePoints, Math.Max(1d, sourceBrushSize / 3d));

            if (sourcePoints.Count == 0)
            {
                return false;
            }

            if (sourcePoints.Count == 1)
            {
                sourcePoints.Add(new RedactionPoint { X = sourcePoints[0].X, Y = sourcePoints[0].Y });
            }

            var radius = sourceBrushSize / 2d;
            var left = Math.Max(0, sourcePoints.Min(point => point.X) - radius);
            var top = Math.Max(0, sourcePoints.Min(point => point.Y) - radius);
            var right = Math.Min(sourceImageWidth, sourcePoints.Max(point => point.X) + radius);
            var bottom = Math.Min(sourceImageHeight, sourcePoints.Max(point => point.Y) + radius);

            if (right - left < 1 || bottom - top < 1)
            {
                return false;
            }

            operation = new RedactionOperation
            {
                X = left,
                Y = top,
                Width = right - left,
                Height = bottom - top,
                PathPoints = sourcePoints,
                BrushSize = (int)Math.Round(sourceBrushSize),
                Mode = SelectedRedactionMode == "高斯模糊" ? RedactionMode.Blur : RedactionMode.Mosaic,
                MosaicBlockSize = MosaicBlockSize,
                BlurRadius = BlurRadius
            };
            return true;
        }

        private static Point ClampPointToBounds(Point point, Rect bounds)
        {
            return new Point(
                Math.Max(bounds.Left, Math.Min(bounds.Right, point.X)),
                Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y)));
        }

        private static List<RedactionPoint> DensifyRedactionPath(IReadOnlyList<RedactionPoint> points, double maxStep)
        {
            if (points == null || points.Count <= 1)
            {
                return points == null ? new List<RedactionPoint>() : points.ToList();
            }

            var result = new List<RedactionPoint> { points[0] };
            var step = Math.Max(1d, maxStep);

            for (var index = 1; index < points.Count; index++)
            {
                var previous = result[result.Count - 1];
                var current = points[index];
                var dx = current.X - previous.X;
                var dy = current.Y - previous.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var segments = Math.Max(1, (int)Math.Ceiling(distance / step));

                for (var segment = 1; segment <= segments; segment++)
                {
                    var t = segment / (double)segments;
                    result.Add(new RedactionPoint
                    {
                        X = previous.X + dx * t,
                        Y = previous.Y + dy * t
                    });
                }
            }

            return result;
        }

        private Rect ResolveDisplayedImageBounds()
        {
            if (PreviewImage == null || RedactionCanvas == null || PreviewImage.Source == null)
            {
                return Rect.Empty;
            }

            var imageWidth = PreviewImage.ActualWidth;
            var imageHeight = PreviewImage.ActualHeight;
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return Rect.Empty;
            }

            var imageSource = PreviewImage.Source;
            var sourceWidth = imageSource.Width;
            var sourceHeight = imageSource.Height;
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return Rect.Empty;
            }

            var imageRatio = sourceWidth / sourceHeight;
            var controlRatio = imageWidth / imageHeight;
            double displayedWidth;
            double displayedHeight;

            if (controlRatio > imageRatio)
            {
                displayedHeight = imageHeight;
                displayedWidth = displayedHeight * imageRatio;
            }
            else
            {
                displayedWidth = imageWidth;
                displayedHeight = displayedWidth / imageRatio;
            }

            Point imageOrigin;
            try
            {
                imageOrigin = PreviewImage.TransformToAncestor(RedactionCanvas).Transform(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                return Rect.Empty;
            }

            return new Rect(
                imageOrigin.X + (imageWidth - displayedWidth) / 2d,
                imageOrigin.Y + (imageHeight - displayedHeight) / 2d,
                displayedWidth,
                displayedHeight);
        }

        private async Task RefreshPreviewAsync()
        {
            var image = SelectedImage;
            if (image == null)
            {
                CancelPreviewRequest();
                EditedPreview = null;
                IsPreviewBusy = false;
                return;
            }

            CancelPreviewRequest();
            var cancellationSource = new CancellationTokenSource();
            _previewCancellationTokenSource = cancellationSource;
            var token = cancellationSource.Token;
            var requestVersion = ++_previewRequestVersion;
            IsPreviewBusy = true;

            try
            {
                var options = CreateOptions();
                _imageService.ValidateOptions(options);

                var checkedImages = Images.Where(x => x.IsSelected).ToList();
                var preview = checkedImages.Count >= 2
                    ? await _imageService.CreateStitchPreviewAsync(checkedImages, options, CreateStitchOptions(), token)
                    : await _imageService.CreateCompressedPreviewAsync(image, options, token);

                if (!token.IsCancellationRequested && requestVersion == _previewRequestVersion && ReferenceEquals(SelectedImage, image))
                {
                    if (checkedImages.Count < 2)
                    {
                        image.CompressedPreview = preview;
                    }

                    EditedPreview = preview;
                    CurrentStatus = checkedImages.Count >= 2 ? "拼接预览已更新" : "预览已更新";
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && requestVersion == _previewRequestVersion)
                {
                    CurrentStatus = "预览失败：" + ex.Message;
                }
            }
            finally
            {
                if (ReferenceEquals(_previewCancellationTokenSource, cancellationSource))
                {
                    _previewCancellationTokenSource = null;
                    IsPreviewBusy = false;
                }

                cancellationSource.Dispose();
            }
        }

        private void CancelPreviewRequest()
        {
            var cancellationSource = _previewCancellationTokenSource;
            _previewCancellationTokenSource = null;
            if (cancellationSource == null)
            {
                return;
            }

            try
            {
                cancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private ProcessingOptions CreateOptions()
        {
            return new ProcessingOptions
            {
                OutputFormat = MapOutputFormat(SelectedOutputFormat),
                CompressionMode = SelectedCompressionMode == "无损压缩" ? CompressionMode.Lossless : CompressionMode.Lossy,
                Quality = Quality,
                TargetSizeEnabled = TargetSizeEnabled,
                TargetSizeBytes = (long)Math.Round(TargetSizeKb * 1024d),
                QuantizePng = QuantizePng,
                PngColorCount = PngColorCount,
                OutputDirectory = OutputDirectory,
                ConflictStrategy = MapConflictStrategy(SelectedConflictStrategy),
                ResizeEnabled = ResizeEnabled,
                ResizeWidth = ResizeWidth,
                ResizeHeight = ResizeHeight,
                ResizeUnit = MapResizeUnit(SelectedResizeUnit),
                ResizeDpi = ResizeDpi,
                KeepAspectRatio = KeepAspectRatio,
                WatermarkEnabled = WatermarkEnabled,
                WatermarkText = WatermarkText,
                WatermarkPosition = MapWatermarkPosition(SelectedWatermarkPosition),
                WatermarkOpacity = WatermarkOpacity,
                WatermarkFontSize = WatermarkFontSize,
                BackgroundProcessingEnabled = BackgroundProcessingEnabled,
                BackgroundTolerance = BackgroundTolerance,
                BackgroundFeather = BackgroundFeather,
                IdPhotoProcessingEnabled = IdPhotoProcessingEnabled,
                IdPhotoBackgroundColor = IdPhotoBackgroundColor
            };
        }

        private StitchOptions CreateStitchOptions()
        {
            return new StitchOptions
            {
                Mode = MapStitchMode(SelectedStitchMode),
                Columns = StitchColumns,
                Spacing = StitchSpacing,
                BackgroundColor = StitchBackgroundColor,
                CanvasWidth = StitchCanvasWidth,
                CanvasHeight = StitchCanvasHeight,
                Padding = StitchPadding,
                FitMode = MapCollageFitMode(SelectedCollageFitMode)
            };
        }

        private PdfExportOptions CreatePdfExportOptions()
        {
            return new PdfExportOptions
            {
                MergeIntoSingleFile = SelectedPdfExportMode != "每张图片输出一个 PDF",
                PageDpi = PdfPageDpi
            };
        }

        private static OutputFormat MapOutputFormat(string value)
        {
            switch (value)
            {
                case "JPEG":
                    return OutputFormat.Jpeg;
                case "PNG":
                    return OutputFormat.Png;
                case "WebP":
                    return OutputFormat.Webp;
                case "BMP":
                    return OutputFormat.Bmp;
                case "GIF":
                    return OutputFormat.Gif;
                case "TIFF":
                    return OutputFormat.Tiff;
                default:
                    return OutputFormat.KeepOriginal;
            }
        }

        private static ConflictStrategy MapConflictStrategy(string value)
        {
            switch (value)
            {
                case "覆盖":
                    return ConflictStrategy.Overwrite;
                case "跳过":
                    return ConflictStrategy.Skip;
                default:
                    return ConflictStrategy.Rename;
            }
        }

        private static WatermarkPosition MapWatermarkPosition(string value)
        {
            switch (value)
            {
                case "左上角":
                    return WatermarkPosition.TopLeft;
                case "右上角":
                    return WatermarkPosition.TopRight;
                case "居中":
                    return WatermarkPosition.Center;
                case "左下角":
                    return WatermarkPosition.BottomLeft;
                default:
                    return WatermarkPosition.BottomRight;
            }
        }

        private static ResizeUnit MapResizeUnit(string value)
        {
            switch (value)
            {
                case "厘米":
                    return ResizeUnit.Centimeter;
                case "毫米":
                    return ResizeUnit.Millimeter;
                case "英寸":
                    return ResizeUnit.Inch;
                default:
                    return ResizeUnit.Pixel;
            }
        }

        private static StitchMode MapStitchMode(string value)
        {
            switch (value)
            {
                case "横向拼接":
                    return StitchMode.Horizontal;
                case "网格拼接":
                case "网格拼贴":
                    return StitchMode.Grid;
                case "瀑布流拼贴":
                    return StitchMode.Waterfall;
                case "主图海报":
                    return StitchMode.Poster;
                case "自由排列":
                    return StitchMode.Free;
                default:
                    return StitchMode.Vertical;
            }
        }

        private static CollageFitMode MapCollageFitMode(string value)
        {
            return value == "完整留白" ? CollageFitMode.Contain : CollageFitMode.Cover;
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}