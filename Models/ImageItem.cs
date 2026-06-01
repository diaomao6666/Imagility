using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Photo_zip.Models
{
    public enum RedactionMode
    {
        Mosaic,
        Blur
    }

    public sealed class RedactionPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class RedactionOperation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public IReadOnlyList<RedactionPoint> PathPoints { get; set; } = Array.Empty<RedactionPoint>();
        public int BrushSize { get; set; } = 32;
        public RedactionMode Mode { get; set; } = RedactionMode.Mosaic;
        public int MosaicBlockSize { get; set; } = 12;
        public int BlurRadius { get; set; } = 8;
    }

    public sealed class SignatureOverlay
    {
        public string FilePath { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
    /// <summary>
    /// 列表中的单张图片状态。它同时承载原始信息、预览信息和处理结果。
    /// </summary>
    public class ImageItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private BitmapImage _thumbnail;
        private BitmapImage _originalPreview;
        private BitmapImage _compressedPreview;
        private long _previewSize;
        private string _status = "待处理";
        private string _outputPath;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string DirectoryPath { get; set; }
        public long OriginalSize { get; set; }
        public string Format { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public BitmapImage Thumbnail
        {
            get { return _thumbnail; }
            set { SetProperty(ref _thumbnail, value); }
        }

        public BitmapImage OriginalPreview
        {
            get { return _originalPreview; }
            set { SetProperty(ref _originalPreview, value); }
        }

        public BitmapImage CompressedPreview
        {
            get { return _compressedPreview; }
            set { SetProperty(ref _compressedPreview, value); }
        }

        public long PreviewSize
        {
            get { return _previewSize; }
            set
            {
                if (SetProperty(ref _previewSize, value))
                {
                    OnPropertyChanged(nameof(PreviewSizeText));
                    OnPropertyChanged(nameof(CompressionRatioText));
                }
            }
        }

        public string Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        public string OutputPath
        {
            get { return _outputPath; }
            set { SetProperty(ref _outputPath, value); }
        }

        private readonly object _redactionLock = new object();
        private readonly List<RedactionOperation> _redactions = new List<RedactionOperation>();
        private readonly object _signatureLock = new object();
        private readonly List<SignatureOverlay> _signatureOverlays = new List<SignatureOverlay>();

        public IReadOnlyList<RedactionOperation> Redactions => GetRedactionSnapshot();
        public IReadOnlyList<SignatureOverlay> SignatureOverlays => GetSignatureSnapshot();

        public bool CanUndoRedaction
        {
            get
            {
                lock (_redactionLock)
                {
                    return _redactions.Count > 0;
                }
            }
        }

        public int RedactionCount
        {
            get
            {
                lock (_redactionLock)
                {
                    return _redactions.Count;
                }
            }
        }

        public string RedactionStatusText => RedactionCount > 0 ? $"已打码 {RedactionCount} 处，可 Ctrl+Z 撤销" : "未添加打码区域";

        public int SignatureCount
        {
            get
            {
                lock (_signatureLock)
                {
                    return _signatureOverlays.Count;
                }
            }
        }

        public string SignatureStatusText => SignatureCount > 0 ? $"已插入 {SignatureCount} 个签名，可拖动或缩放" : "未插入签名";

        public IReadOnlyList<RedactionOperation> GetRedactionSnapshot()
        {
            lock (_redactionLock)
            {
                return _redactions.Select(CloneRedaction).ToArray();
            }
        }

        public void AddRedaction(RedactionOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            lock (_redactionLock)
            {
                _redactions.Add(CloneRedaction(operation));
            }

            NotifyRedactionsChanged();
        }

        public bool UndoLastRedaction()
        {
            lock (_redactionLock)
            {
                if (_redactions.Count == 0)
                {
                    return false;
                }

                _redactions.RemoveAt(_redactions.Count - 1);
            }

            NotifyRedactionsChanged();
            return true;
        }

        public void NotifyRedactionsChanged()
        {
            OnPropertyChanged(nameof(CanUndoRedaction));
            OnPropertyChanged(nameof(RedactionCount));
            OnPropertyChanged(nameof(RedactionStatusText));
            OnPropertyChanged(nameof(Redactions));
        }

        public IReadOnlyList<SignatureOverlay> GetSignatureSnapshot()
        {
            lock (_signatureLock)
            {
                return _signatureOverlays.Select(CloneSignatureOverlay).ToArray();
            }
        }

        public void AddSignatureOverlay(SignatureOverlay overlay)
        {
            if (overlay == null || string.IsNullOrWhiteSpace(overlay.FilePath))
            {
                return;
            }

            lock (_signatureLock)
            {
                _signatureOverlays.Add(CloneSignatureOverlay(overlay));
            }

            NotifySignaturesChanged();
        }

        public void ReplaceSignatureOverlays(IEnumerable<SignatureOverlay> overlays)
        {
            lock (_signatureLock)
            {
                _signatureOverlays.Clear();
                if (overlays != null)
                {
                    _signatureOverlays.AddRange(overlays.Where(x => x != null).Select(CloneSignatureOverlay));
                }
            }

            NotifySignaturesChanged();
        }

        public void NotifySignaturesChanged()
        {
            OnPropertyChanged(nameof(SignatureCount));
            OnPropertyChanged(nameof(SignatureStatusText));
            OnPropertyChanged(nameof(SignatureOverlays));
        }

        private static RedactionOperation CloneRedaction(RedactionOperation source)
        {
            return new RedactionOperation
            {
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                PathPoints = source.PathPoints == null
                    ? Array.Empty<RedactionPoint>()
                    : source.PathPoints.Select(point => new RedactionPoint { X = point.X, Y = point.Y }).ToArray(),
                BrushSize = source.BrushSize,
                Mode = source.Mode,
                MosaicBlockSize = source.MosaicBlockSize,
                BlurRadius = source.BlurRadius
            };
        }

        private static SignatureOverlay CloneSignatureOverlay(SignatureOverlay source)
        {
            return new SignatureOverlay
            {
                FilePath = source.FilePath,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height
            };
        }

        public string OriginalSizeText => FormatBytes(OriginalSize);
        public string PreviewSizeText => PreviewSize > 0 ? FormatBytes(PreviewSize) : "-";
        public string DimensionText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "-";

        public string CompressionRatioText
        {
            get
            {
                if (OriginalSize <= 0 || PreviewSize <= 0)
                {
                    return "-";
                }

                var saved = 1d - (PreviewSize / (double)OriginalSize);
                return saved >= 0 ? $"节省 {saved:P1}" : $"增加 {Math.Abs(saved):P1}";
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            string[] units = { "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unitIndex = -1;

            do
            {
                value /= 1024;
                unitIndex++;
            }
            while (value >= 1024 && unitIndex < units.Length - 1);

            return $"{value:0.##} {units[unitIndex]}";
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