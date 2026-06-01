using Photo_zip.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WpfColor = System.Windows.Media.Color;
using WpfDrawingVisual = System.Windows.Media.DrawingVisual;
using WpfFormattedText = System.Windows.Media.FormattedText;
using WpfPixelFormats = System.Windows.Media.PixelFormats;
using WpfPngBitmapEncoder = System.Windows.Media.Imaging.PngBitmapEncoder;
using WpfPoint = System.Windows.Point;
using WpfRenderTargetBitmap = System.Windows.Media.Imaging.RenderTargetBitmap;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfTypeface = System.Windows.Media.Typeface;

namespace Photo_zip.Services
{
    public enum OutputFormat
    {
        KeepOriginal,
        Jpeg,
        Png,
        Webp,
        Bmp,
        Gif,
        Tiff
    }

    public enum CompressionMode
    {
        Lossless,
        Lossy
    }

    public enum ConflictStrategy
    {
        Overwrite,
        Rename,
        Skip
    }

    public enum WatermarkPosition
    {
        TopLeft,
        TopRight,
        Center,
        BottomLeft,
        BottomRight
    }

    public enum BackgroundAction
    {
        RemoveToTransparent,
        ReplaceWithColor
    }

    public enum ResizeUnit
    {
        Pixel,
        Centimeter,
        Millimeter,
        Inch
    }

    public enum StitchMode
    {
        Vertical,
        Horizontal,
        Grid,
        Waterfall,
        Poster,
        Free
    }

    public enum CollageFitMode
    {
        Contain,
        Cover
    }

    public sealed class ProcessingOptions
    {
        public OutputFormat OutputFormat { get; set; } = OutputFormat.KeepOriginal;
        public CompressionMode CompressionMode { get; set; } = CompressionMode.Lossy;
        public int Quality { get; set; } = 80;
        public bool QuantizePng { get; set; }
        public int PngColorCount { get; set; } = 256;
        public string OutputDirectory { get; set; }
        public ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.Rename;
        public bool ResizeEnabled { get; set; }
        public double ResizeWidth { get; set; } = 1920;
        public double ResizeHeight { get; set; } = 1080;
        public ResizeUnit ResizeUnit { get; set; } = ResizeUnit.Pixel;
        public int ResizeDpi { get; set; } = 300;
        public bool KeepAspectRatio { get; set; } = true;
        public bool WatermarkEnabled { get; set; }
        public string WatermarkText { get; set; }
        public WatermarkPosition WatermarkPosition { get; set; } = WatermarkPosition.BottomRight;
        public int WatermarkOpacity { get; set; } = 35;
        public int WatermarkFontSize { get; set; } = 36;
        public bool BackgroundProcessingEnabled { get; set; }
        public BackgroundAction BackgroundAction { get; set; } = BackgroundAction.RemoveToTransparent;
        public int BackgroundTolerance { get; set; } = 28;
        public int BackgroundFeather { get; set; } = 12;
        public string BackgroundReplacementColor { get; set; } = "#FFFFFF";
    }

    public sealed class ProcessingProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string CurrentFileName { get; set; }
        public string Message { get; set; }
    }

    public sealed class StitchOptions
    {
        public StitchMode Mode { get; set; } = StitchMode.Vertical;
        public int Columns { get; set; } = 2;
        public int Spacing { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public int CanvasWidth { get; set; } = 1600;
        public int CanvasHeight { get; set; } = 1200;
        public int Padding { get; set; } = 24;
        public CollageFitMode FitMode { get; set; } = CollageFitMode.Cover;
    }

    public sealed class PdfExportOptions
    {
        public bool MergeIntoSingleFile { get; set; } = true;
        public int PageDpi { get; set; } = 96;
    }

    internal sealed class PdfPageImage
    {
        public byte[] JpegBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double PageWidthPoints { get; set; }
        public double PageHeightPoints { get; set; }
    }

    /// <summary>
    /// 集中处理图片识别、预览压缩和批量输出。界面层只负责收集参数与展示状态。
    /// </summary>
    public class ImageProcessingService
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"
        };

        public bool IsSupportedImage(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && SupportedExtensions.Contains(Path.GetExtension(path));
        }

        public IEnumerable<string> ExpandFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (File.Exists(path) && IsSupportedImage(path))
                {
                    yield return path;
                    continue;
                }

                if (!Directory.Exists(path))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(IsSupportedImage).ToList();
                }
                catch
                {
                    files = Enumerable.Empty<string>();
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        public Task<ImageItem> CreateImageItemAsync(string filePath)
        {
            return Task.Run(() =>
            {
                IImageFormat format;
                using (var image = LoadOrientedImage(filePath, out format))
                {
                    var fileInfo = new FileInfo(filePath);
                    return new ImageItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath),
                        OriginalSize = fileInfo.Length,
                        Format = NormalizeFormatName(format?.Name, Path.GetExtension(filePath)),
                        Width = image.Width,
                        Height = image.Height,
                        Thumbnail = CreateBitmapPreview(image, 96),
                        OriginalPreview = CreateBitmapPreview(image, 900)
                    };
                }
            });
        }

        public Task<BitmapImage> CreateCompressedPreviewAsync(ImageItem item, ProcessingOptions options, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var stream = EncodeToMemory(item, options, preview: true))
                {
                    item.PreviewSize = stream.Length;
                    stream.Position = 0;
                    return LoadBitmapFromStream(stream);
                }
            }, cancellationToken);
        }

        public Task<BitmapImage> CreateStitchPreviewAsync(IEnumerable<ImageItem> items, ProcessingOptions options, StitchOptions stitchOptions, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                ValidateOptions(options);
                ValidateStitchOptions(stitchOptions);

                var list = items?.Where(x => x.IsSelected).ToList() ?? new List<ImageItem>();
                if (list.Count < 2)
                {
                    throw new InvalidOperationException("请至少勾选 2 张图片预览拼接效果。");
                }

                var loadedImages = new List<Image<Rgba32>>();
                try
                {
                    foreach (var item in list)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var image = LoadOrientedImage(item.FilePath);
                        ApplyTransforms(image, options, item.GetRedactionSnapshot(), item.GetSignatureSnapshot());
                        loadedImages.Add(image);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    using (var stitched = CreateStitchedImage(loadedImages, stitchOptions))
                    {
                        ResizeForPreview(stitched);
                        using (var stream = new MemoryStream())
                        {
                            stitched.Save(stream, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
                            stream.Position = 0;
                            return LoadBitmapFromStream(stream);
                        }
                    }
                }
                finally
                {
                    foreach (var image in loadedImages)
                    {
                        image.Dispose();
                    }
                }
            }, cancellationToken);
        }

        public Task ProcessBatchAsync(IEnumerable<ImageItem> items, ProcessingOptions options, IProgress<ProcessingProgress> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var list = items?.Where(x => x.IsSelected).ToList() ?? new List<ImageItem>();
                var total = list.Count;
                var completed = 0;

                foreach (var item in list)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ProcessingProgress
                    {
                        Completed = completed,
                        Total = total,
                        CurrentFileName = item.FileName,
                        Message = "正在处理"
                    });

                    try
                    {
                        var outputPath = BuildOutputPath(item, options);
                        if (string.IsNullOrWhiteSpace(outputPath))
                        {
                            item.Status = "已跳过";
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                            using (var image = LoadOrientedImage(item.FilePath))
                            {
                                ApplyTransforms(image, options, item.GetRedactionSnapshot(), item.GetSignatureSnapshot());
                                var encoder = CreateEncoder(ResolveOutputFormat(item, options), options);
                                image.Save(outputPath, encoder);
                            }

                            item.OutputPath = outputPath;
                            item.Status = "完成";
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Status = "失败：" + ex.Message;
                    }

                    completed++;
                    progress?.Report(new ProcessingProgress
                    {
                        Completed = completed,
                        Total = total,
                        CurrentFileName = item.FileName,
                        Message = "已完成"
                    });
                }
            }, cancellationToken);
        }

        public Task<string> StitchImagesAsync(IEnumerable<ImageItem> items, ProcessingOptions options, StitchOptions stitchOptions, IProgress<ProcessingProgress> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                ValidateOptions(options);
                ValidateStitchOptions(stitchOptions);

                var list = items?.Where(x => x.IsSelected).ToList() ?? new List<ImageItem>();
                if (list.Count < 2)
                {
                    throw new InvalidOperationException("请至少选择 2 张图片进行合并/拼接。");
                }

                var loadedImages = new List<Image<Rgba32>>();
                try
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = list[i];
                        progress?.Report(new ProcessingProgress
                        {
                            Completed = i,
                            Total = list.Count + 1,
                            CurrentFileName = item.FileName,
                            Message = "正在准备拼接"
                        });

                        var image = LoadOrientedImage(item.FilePath);
                        ApplyTransforms(image, options, item.GetRedactionSnapshot(), item.GetSignatureSnapshot());
                        loadedImages.Add(image);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ProcessingProgress
                    {
                        Completed = list.Count,
                        Total = list.Count + 1,
                        CurrentFileName = "合并图片",
                        Message = "正在生成拼接图"
                    });

                    var outputFormat = ResolveOutputFormatForStitch(options);
                    var outputPath = BuildStitchOutputPath(list[0], options, outputFormat);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    using (var stitched = CreateStitchedImage(loadedImages, stitchOptions))
                    {
                        var encoder = CreateEncoder(outputFormat, options);
                        stitched.Save(outputPath, encoder);
                    }

                    foreach (var item in list)
                    {
                        item.OutputPath = outputPath;
                        item.Status = "已拼接";
                    }

                    progress?.Report(new ProcessingProgress
                    {
                        Completed = list.Count + 1,
                        Total = list.Count + 1,
                        CurrentFileName = Path.GetFileName(outputPath),
                        Message = "拼接完成"
                    });

                    return outputPath;
                }
                finally
                {
                    foreach (var image in loadedImages)
                    {
                        image.Dispose();
                    }
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<string>> ExportPdfAsync(IEnumerable<ImageItem> items, ProcessingOptions options, PdfExportOptions pdfOptions, IProgress<ProcessingProgress> progress, CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                ValidateOptions(options);
                ValidatePdfOptions(pdfOptions);

                var list = items?.ToList() ?? new List<ImageItem>();
                if (!list.Any())
                {
                    throw new InvalidOperationException("请先导入需要导出 PDF 的图片。");
                }

                return pdfOptions.MergeIntoSingleFile
                    ? ExportMergedPdf(list, options, pdfOptions, progress, cancellationToken)
                    : ExportSeparatePdfs(list, options, pdfOptions, progress, cancellationToken);
            }, cancellationToken);
        }

        public void ValidateOptions(ProcessingOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Quality = Math.Max(0, Math.Min(100, options.Quality));
            options.PngColorCount = Math.Max(2, Math.Min(256, options.PngColorCount));
            options.ResizeWidth = Math.Max(0.01, Math.Min(20000, options.ResizeWidth));
            options.ResizeHeight = Math.Max(0.01, Math.Min(20000, options.ResizeHeight));
            options.ResizeDpi = Math.Max(1, Math.Min(2400, options.ResizeDpi));
            options.WatermarkOpacity = Math.Max(1, Math.Min(100, options.WatermarkOpacity));
            options.WatermarkFontSize = Math.Max(8, Math.Min(240, options.WatermarkFontSize));
            options.BackgroundTolerance = Math.Max(0, Math.Min(255, options.BackgroundTolerance));
            options.BackgroundFeather = Math.Max(0, Math.Min(100, options.BackgroundFeather));

            if (options.WatermarkEnabled && string.IsNullOrWhiteSpace(options.WatermarkText))
            {
                throw new ArgumentException("启用水印时，请输入水印文字。");
            }

            if (options.BackgroundProcessingEnabled)
            {
                ParseHexColor(options.BackgroundReplacementColor);
            }

            if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                var invalidChars = Path.GetInvalidPathChars();
                if (options.OutputDirectory.IndexOfAny(invalidChars) >= 0)
                {
                    throw new ArgumentException("输出路径包含非法字符。");
                }
            }
        }

        public void ValidateStitchOptions(StitchOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Columns = Math.Max(1, Math.Min(20, options.Columns));
            options.Spacing = Math.Max(0, Math.Min(1000, options.Spacing));
            options.CanvasWidth = Math.Max(200, Math.Min(20000, options.CanvasWidth));
            options.CanvasHeight = Math.Max(200, Math.Min(20000, options.CanvasHeight));
            options.Padding = Math.Max(0, Math.Min(Math.Min(options.CanvasWidth, options.CanvasHeight) / 3, options.Padding));
            ParseHexColor(options.BackgroundColor);
        }

        public void ValidatePdfOptions(PdfExportOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.PageDpi = Math.Max(72, Math.Min(1200, options.PageDpi));
        }

        private IReadOnlyList<string> ExportMergedPdf(IReadOnlyList<ImageItem> list, ProcessingOptions options, PdfExportOptions pdfOptions, IProgress<ProcessingProgress> progress, CancellationToken cancellationToken)
        {
            var outputPath = BuildPdfOutputPath(list[0], options, "archive");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                foreach (var item in list)
                {
                    item.Status = "已跳过";
                }

                return new List<string>();
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            WritePdf(outputPath, list.Count, index =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = list[index];
                progress?.Report(new ProcessingProgress
                {
                    Completed = index,
                    Total = list.Count + 1,
                    CurrentFileName = item.FileName,
                    Message = "正在写入 PDF 页面"
                });

                var page = CreatePdfPageImage(item, options, pdfOptions);
                item.Status = "已加入 PDF";
                return page;
            });

            foreach (var item in list)
            {
                item.OutputPath = outputPath;
                item.Status = "PDF 完成";
            }

            progress?.Report(new ProcessingProgress
            {
                Completed = list.Count + 1,
                Total = list.Count + 1,
                CurrentFileName = Path.GetFileName(outputPath),
                Message = "PDF 导出完成"
            });

            return new[] { outputPath };
        }

        private IReadOnlyList<string> ExportSeparatePdfs(IReadOnlyList<ImageItem> list, ProcessingOptions options, PdfExportOptions pdfOptions, IProgress<ProcessingProgress> progress, CancellationToken cancellationToken)
        {
            var outputPaths = new List<string>();

            for (var i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = list[i];
                progress?.Report(new ProcessingProgress
                {
                    Completed = i,
                    Total = list.Count,
                    CurrentFileName = item.FileName,
                    Message = "正在导出 PDF"
                });

                try
                {
                    var outputPath = BuildPdfOutputPath(item, options, Path.GetFileNameWithoutExtension(item.FileName));
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        item.Status = "已跳过";
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                        var page = CreatePdfPageImage(item, options, pdfOptions);
                        WritePdf(outputPath, new[] { page });
                        item.OutputPath = outputPath;
                        item.Status = "PDF 完成";
                        outputPaths.Add(outputPath);
                    }
                }
                catch (Exception ex)
                {
                    item.Status = "PDF 失败：" + ex.Message;
                }

                progress?.Report(new ProcessingProgress
                {
                    Completed = i + 1,
                    Total = list.Count,
                    CurrentFileName = item.FileName,
                    Message = "PDF 导出完成"
                });
            }

            return outputPaths;
        }

        private PdfPageImage CreatePdfPageImage(ImageItem item, ProcessingOptions options, PdfExportOptions pdfOptions)
        {
            using (var image = LoadOrientedImage(item.FilePath))
            {
                ApplyTransforms(image, options, item.GetRedactionSnapshot(), item.GetSignatureSnapshot());
                FlattenForPdf(image);

                using (var stream = new MemoryStream())
                {
                    image.Save(stream, new JpegEncoder { Quality = Math.Max(1, Math.Min(100, options.Quality)) });
                    var pageDpi = Math.Max(72, pdfOptions.PageDpi);
                    return new PdfPageImage
                    {
                        JpegBytes = stream.ToArray(),
                        Width = image.Width,
                        Height = image.Height,
                        PageWidthPoints = image.Width * 72d / pageDpi,
                        PageHeightPoints = image.Height * 72d / pageDpi
                    };
                }
            }
        }

        private MemoryStream EncodeToMemory(ImageItem item, ProcessingOptions options, bool preview)
        {
            using (var image = LoadOrientedImage(item.FilePath))
            {
                ApplyTransforms(image, options, item.GetRedactionSnapshot(), item.GetSignatureSnapshot());

                if (preview)
                {
                    ResizeForPreview(image);
                }

                var format = ResolveOutputFormat(item, options);
                var encoder = CreateEncoder(format, options);
                var stream = new MemoryStream();
                image.Save(stream, encoder);
                stream.Position = 0;
                return stream;
            }
        }

        private string BuildOutputPath(ImageItem item, ProcessingOptions options)
        {
            var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(item.DirectoryPath, "Converted")
                : options.OutputDirectory;
            var outputFormat = ResolveOutputFormat(item, options);
            var extension = GetExtension(outputFormat);
            var name = Path.GetFileNameWithoutExtension(item.FileName) + extension;
            var candidate = Path.Combine(outputDirectory, name);

            if (!File.Exists(candidate))
            {
                return candidate;
            }

            if (options.ConflictStrategy == ConflictStrategy.Skip)
            {
                return null;
            }

            if (options.ConflictStrategy == ConflictStrategy.Overwrite)
            {
                return candidate;
            }

            var index = 1;
            string renamed;
            do
            {
                renamed = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(item.FileName)} ({index}){extension}");
                index++;
            }
            while (File.Exists(renamed));

            return renamed;
        }

        private string BuildStitchOutputPath(ImageItem firstItem, ProcessingOptions options, OutputFormat outputFormat)
        {
            var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(firstItem.DirectoryPath, "Converted")
                : options.OutputDirectory;
            var extension = GetExtension(outputFormat);
            var candidate = Path.Combine(outputDirectory, "stitched" + extension);

            if (!File.Exists(candidate))
            {
                return candidate;
            }

            if (options.ConflictStrategy == ConflictStrategy.Skip)
            {
                throw new IOException("拼接输出文件已存在，当前冲突策略为跳过。");
            }

            if (options.ConflictStrategy == ConflictStrategy.Overwrite)
            {
                return candidate;
            }

            var index = 1;
            string renamed;
            do
            {
                renamed = Path.Combine(outputDirectory, $"stitched ({index}){extension}");
                index++;
            }
            while (File.Exists(renamed));

            return renamed;
        }

        private string BuildPdfOutputPath(ImageItem item, ProcessingOptions options, string baseName)
        {
            var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(item.DirectoryPath, "Converted")
                : options.OutputDirectory;
            var safeBaseName = string.IsNullOrWhiteSpace(baseName) ? "archive" : baseName;
            var candidate = Path.Combine(outputDirectory, safeBaseName + ".pdf");

            if (!File.Exists(candidate))
            {
                return candidate;
            }

            if (options.ConflictStrategy == ConflictStrategy.Skip)
            {
                return null;
            }

            if (options.ConflictStrategy == ConflictStrategy.Overwrite)
            {
                return candidate;
            }

            var index = 1;
            string renamed;
            do
            {
                renamed = Path.Combine(outputDirectory, $"{safeBaseName} ({index}).pdf");
                index++;
            }
            while (File.Exists(renamed));

            return renamed;
        }

        private OutputFormat ResolveOutputFormat(ImageItem item, ProcessingOptions options)
        {
            var format = options.OutputFormat == OutputFormat.KeepOriginal ? FormatFromName(item.Format, item.FilePath) : options.OutputFormat;
            return ResolveAlphaSafeFormat(format, options);
        }

        private OutputFormat ResolveOutputFormat(string filePath, ProcessingOptions options)
        {
            var format = options.OutputFormat == OutputFormat.KeepOriginal ? FormatFromExtension(Path.GetExtension(filePath)) : options.OutputFormat;
            return ResolveAlphaSafeFormat(format, options);
        }

        private OutputFormat ResolveOutputFormatForStitch(ProcessingOptions options)
        {
            var format = options.OutputFormat == OutputFormat.KeepOriginal ? OutputFormat.Png : options.OutputFormat;
            return ResolveAlphaSafeFormat(format, options);
        }

        private static OutputFormat ResolveAlphaSafeFormat(OutputFormat format, ProcessingOptions options)
        {
            if (options.BackgroundProcessingEnabled
                && options.BackgroundAction == BackgroundAction.RemoveToTransparent
                && !SupportsAlpha(format))
            {
                return OutputFormat.Png;
            }

            return format;
        }

        private static bool SupportsAlpha(OutputFormat format)
        {
            return format == OutputFormat.Png || format == OutputFormat.Webp || format == OutputFormat.Gif || format == OutputFormat.Tiff;
        }

        private static void ApplyTransforms(Image<Rgba32> image, ProcessingOptions options, IReadOnlyList<RedactionOperation> redactions, IReadOnlyList<SignatureOverlay> signatures)
        {
            ApplyRedactions(image, redactions);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            if (options.ResizeEnabled)
            {
                var targetSize = ResolveResizePixelSize(options);
                var mode = options.KeepAspectRatio ? ResizeMode.Max : ResizeMode.Stretch;

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = targetSize,
                    Mode = mode
                }));
            }

            if (options.BackgroundProcessingEnabled)
            {
                ApplyBackgroundProcessing(image, options);
            }

            ApplySignatures(image, signatures, originalWidth, originalHeight);

            if (options.WatermarkEnabled && !string.IsNullOrWhiteSpace(options.WatermarkText))
            {
                using (var watermark = CreateWatermarkImage(image.Width, image.Height, options))
                {
                    image.Mutate(x => x.DrawImage(watermark, 1f));
                }
            }
        }

        private const int RedactionTileSize = 512;
        private const long RedactionDenseMaskPixelLimit = 4_000_000L;

        private static void ApplySignatures(Image<Rgba32> image, IReadOnlyList<SignatureOverlay> signatures, int sourceWidth, int sourceHeight)
        {
            if (signatures == null || signatures.Count == 0 || sourceWidth <= 0 || sourceHeight <= 0)
            {
                return;
            }

            var scaleX = image.Width / (double)sourceWidth;
            var scaleY = image.Height / (double)sourceHeight;
            foreach (var signature in signatures)
            {
                if (signature == null || string.IsNullOrWhiteSpace(signature.FilePath) || !File.Exists(signature.FilePath))
                {
                    continue;
                }

                var targetWidth = Math.Max(1, (int)Math.Round(signature.Width * scaleX));
                var targetHeight = Math.Max(1, (int)Math.Round(signature.Height * scaleY));
                var targetX = Math.Max(0, Math.Min(image.Width - targetWidth, (int)Math.Round(signature.X * scaleX)));
                var targetY = Math.Max(0, Math.Min(image.Height - targetHeight, (int)Math.Round(signature.Y * scaleY)));

                try
                {
                    using (var signatureImage = Image.Load<Rgba32>(signature.FilePath))
                    {
                        signatureImage.Mutate(context => context.Resize(new ResizeOptions
                        {
                            Size = new Size(targetWidth, targetHeight),
                            Mode = ResizeMode.Stretch
                        }));

                        image.Mutate(context => context.DrawImage(signatureImage, new Point(targetX, targetY), 1f));
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private static void ApplyRedactions(Image<Rgba32> image, IReadOnlyList<RedactionOperation> redactions)
        {
            if (redactions == null || redactions.Count == 0)
            {
                return;
            }

            foreach (var redaction in redactions)
            {
                var rectangle = NormalizeRedactionRectangle(image, redaction);
                if (rectangle.Width <= 0 || rectangle.Height <= 0)
                {
                    continue;
                }

                ApplyRedaction(image, redaction, rectangle);
            }
        }

        private static void ApplyRedaction(Image<Rgba32> image, RedactionOperation redaction, Rectangle rectangle)
        {
            var area = (long)rectangle.Width * rectangle.Height;
            if (area > RedactionDenseMaskPixelLimit && redaction.PathPoints != null && redaction.PathPoints.Count > 0)
            {
                ApplyRedactionInTiles(image, redaction, rectangle);
                return;
            }

            var mask = CreateRedactionMask(rectangle, redaction);
            if (!MaskHasPaint(mask))
            {
                return;
            }

            if (redaction.Mode == RedactionMode.Blur)
            {
                var radius = Math.Max(1, Math.Min(20, redaction.BlurRadius));
                using (var blurred = image.Clone(context => context.Crop(rectangle).GaussianBlur(radius)))
                {
                    ApplyMaskedRegion(image, blurred, rectangle, mask);
                }
            }
            else
            {
                ApplyMosaicRedaction(image, rectangle, redaction.MosaicBlockSize, mask);
            }
        }

        private static void ApplyRedactionInTiles(Image<Rgba32> image, RedactionOperation redaction, Rectangle rectangle)
        {
            for (var y = rectangle.Y; y < rectangle.Bottom; y += RedactionTileSize)
            {
                var tileHeight = Math.Min(RedactionTileSize, rectangle.Bottom - y);
                for (var x = rectangle.X; x < rectangle.Right; x += RedactionTileSize)
                {
                    var tileWidth = Math.Min(RedactionTileSize, rectangle.Right - x);
                    var tile = new Rectangle(x, y, tileWidth, tileHeight);
                    var mask = CreateRedactionMask(tile, redaction);
                    if (!MaskHasPaint(mask))
                    {
                        continue;
                    }

                    if (redaction.Mode == RedactionMode.Blur)
                    {
                        var radius = Math.Max(1, Math.Min(20, redaction.BlurRadius));
                        var blurBounds = ExpandRectangle(tile, radius * 2, image.Width, image.Height);
                        using (var blurred = image.Clone(context => context.Crop(blurBounds).GaussianBlur(radius)))
                        {
                            ApplyMaskedRegion(image, blurred, tile, blurBounds, mask);
                        }
                    }
                    else
                    {
                        ApplyMosaicRedaction(image, tile, redaction.MosaicBlockSize, mask);
                    }
                }
            }
        }

        private static Rectangle NormalizeRedactionRectangle(Image<Rgba32> image, RedactionOperation redaction)
        {
            if (redaction == null)
            {
                return Rectangle.Empty;
            }

            var left = Math.Max(0, Math.Min(image.Width, (int)Math.Floor(redaction.X)));
            var top = Math.Max(0, Math.Min(image.Height, (int)Math.Floor(redaction.Y)));
            var right = Math.Max(0, Math.Min(image.Width, (int)Math.Ceiling(redaction.X + redaction.Width)));
            var bottom = Math.Max(0, Math.Min(image.Height, (int)Math.Ceiling(redaction.Y + redaction.Height)));
            return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        private static bool[,] CreateRedactionMask(Rectangle rectangle, RedactionOperation redaction)
        {
            var mask = new bool[rectangle.Width, rectangle.Height];
            var points = redaction.PathPoints == null ? Array.Empty<RedactionPoint>() : redaction.PathPoints.ToArray();
            var brushSize = Math.Max(1, Math.Min(1000, redaction.BrushSize));
            var radius = brushSize / 2d;

            if (points.Length == 0)
            {
                FillRectangleMask(mask);
                return mask;
            }

            if (points.Length == 1)
            {
                PaintMaskCircle(mask, points[0].X - rectangle.X, points[0].Y - rectangle.Y, radius);
                return mask;
            }

            for (var index = 1; index < points.Length; index++)
            {
                PaintMaskSegment(
                    mask,
                    points[index - 1].X - rectangle.X,
                    points[index - 1].Y - rectangle.Y,
                    points[index].X - rectangle.X,
                    points[index].Y - rectangle.Y,
                    radius);
            }

            return mask;
        }

        private static void FillRectangleMask(bool[,] mask)
        {
            var width = mask.GetLength(0);
            var height = mask.GetLength(1);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    mask[x, y] = true;
                }
            }
        }

        private static void PaintMaskCircle(bool[,] mask, double centerX, double centerY, double radius)
        {
            var minX = Math.Max(0, (int)Math.Floor(centerX - radius));
            var minY = Math.Max(0, (int)Math.Floor(centerY - radius));
            var maxX = Math.Min(mask.GetLength(0) - 1, (int)Math.Ceiling(centerX + radius));
            var maxY = Math.Min(mask.GetLength(1) - 1, (int)Math.Ceiling(centerY + radius));
            var radiusSquared = radius * radius;

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    if (dx * dx + dy * dy <= radiusSquared)
                    {
                        mask[x, y] = true;
                    }
                }
            }
        }

        private static void PaintMaskSegment(bool[,] mask, double startX, double startY, double endX, double endY, double radius)
        {
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(startX, endX) - radius));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(startY, endY) - radius));
            var maxX = Math.Min(mask.GetLength(0) - 1, (int)Math.Ceiling(Math.Max(startX, endX) + radius));
            var maxY = Math.Min(mask.GetLength(1) - 1, (int)Math.Ceiling(Math.Max(startY, endY) + radius));
            var segmentX = endX - startX;
            var segmentY = endY - startY;
            var lengthSquared = segmentX * segmentX + segmentY * segmentY;
            var radiusSquared = radius * radius;

            if (lengthSquared <= 0.001d)
            {
                PaintMaskCircle(mask, startX, startY, radius);
                return;
            }

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var t = ((x - startX) * segmentX + (y - startY) * segmentY) / lengthSquared;
                    t = Math.Max(0d, Math.Min(1d, t));
                    var nearestX = startX + t * segmentX;
                    var nearestY = startY + t * segmentY;
                    var dx = x - nearestX;
                    var dy = y - nearestY;
                    if (dx * dx + dy * dy <= radiusSquared)
                    {
                        mask[x, y] = true;
                    }
                }
            }
        }

        private static bool MaskHasPaint(bool[,] mask)
        {
            var width = mask.GetLength(0);
            var height = mask.GetLength(1);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (mask[x, y])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ApplyMaskedRegion(Image<Rgba32> image, Image<Rgba32> region, Rectangle rectangle, bool[,] mask)
        {
            ApplyMaskedRegion(image, region, rectangle, rectangle, mask);
        }

        private static void ApplyMaskedRegion(Image<Rgba32> image, Image<Rgba32> region, Rectangle targetRectangle, Rectangle regionSourceBounds, bool[,] mask)
        {
            var pixels = new Rgba32[targetRectangle.Width * targetRectangle.Height];
            region.ProcessPixelRows(sourceAccessor =>
            {
                var sourceX = targetRectangle.X - regionSourceBounds.X;
                var sourceY = targetRectangle.Y - regionSourceBounds.Y;
                for (var y = 0; y < targetRectangle.Height; y++)
                {
                    sourceAccessor.GetRowSpan(sourceY + y).Slice(sourceX, targetRectangle.Width).CopyTo(pixels.AsSpan(y * targetRectangle.Width, targetRectangle.Width));
                }
            });

            image.ProcessPixelRows(targetAccessor =>
            {
                for (var y = 0; y < targetRectangle.Height; y++)
                {
                    var targetRow = targetAccessor.GetRowSpan(targetRectangle.Y + y);
                    var sourceOffset = y * targetRectangle.Width;
                    for (var x = 0; x < targetRectangle.Width; x++)
                    {
                        if (mask[x, y])
                        {
                            targetRow[targetRectangle.X + x] = pixels[sourceOffset + x];
                        }
                    }
                }
            });
        }

        private static Rectangle ExpandRectangle(Rectangle rectangle, int padding, int maxWidth, int maxHeight)
        {
            var left = Math.Max(0, rectangle.X - padding);
            var top = Math.Max(0, rectangle.Y - padding);
            var right = Math.Min(maxWidth, rectangle.Right + padding);
            var bottom = Math.Min(maxHeight, rectangle.Bottom + padding);
            return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        private static void ApplyMosaicRedaction(Image<Rgba32> image, Rectangle rectangle, int blockSize, bool[,] mask)
        {
            var size = Math.Max(2, Math.Min(64, blockSize));
            var sampledWidth = Math.Max(1, (int)Math.Ceiling(rectangle.Width / (double)size));
            var sampledHeight = Math.Max(1, (int)Math.Ceiling(rectangle.Height / (double)size));

            using (var region = image.Clone(context => context.Crop(rectangle)))
            {
                region.Mutate(context => context
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(sampledWidth, sampledHeight),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.NearestNeighbor
                    })
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(rectangle.Width, rectangle.Height),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.NearestNeighbor
                    }));

                ApplyMaskedRegion(image, region, rectangle, mask);
            }
        }

        private static Size ResolveResizePixelSize(ProcessingOptions options)
        {
            return new Size(
                ConvertResizeValueToPixels(options.ResizeWidth, options.ResizeUnit, options.ResizeDpi),
                ConvertResizeValueToPixels(options.ResizeHeight, options.ResizeUnit, options.ResizeDpi));
        }

        private static int ConvertResizeValueToPixels(double value, ResizeUnit unit, int dpi)
        {
            double pixels;
            switch (unit)
            {
                case ResizeUnit.Centimeter:
                    pixels = value / 2.54d * dpi;
                    break;
                case ResizeUnit.Millimeter:
                    pixels = value / 25.4d * dpi;
                    break;
                case ResizeUnit.Inch:
                    pixels = value * dpi;
                    break;
                default:
                    pixels = value;
                    break;
            }

            return Math.Max(1, Math.Min(20000, (int)Math.Round(pixels)));
        }

        private static Image<Rgba32> CreateStitchedImage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            switch (options.Mode)
            {
                case StitchMode.Grid:
                    return CreateGridCollage(images, options);
                case StitchMode.Waterfall:
                    return CreateWaterfallCollage(images, options);
                case StitchMode.Poster:
                    return CreatePosterCollage(images, options);
                case StitchMode.Free:
                    return CreateFreeCollage(images, options);
                default:
                    return CreateLinearCollage(images, options);
            }
        }

        private static Image<Rgba32> CreateLinearCollage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            var spacing = Math.Max(0, options.Spacing);
            var isHorizontal = options.Mode == StitchMode.Horizontal;
            var canvasWidth = isHorizontal
                ? images.Sum(image => image.Width) + spacing * Math.Max(0, images.Count - 1)
                : images.Max(image => image.Width);
            var canvasHeight = isHorizontal
                ? images.Max(image => image.Height)
                : images.Sum(image => image.Height) + spacing * Math.Max(0, images.Count - 1);
            var canvas = CreateCollageCanvas(canvasWidth, canvasHeight, options);

            var offset = 0;
            foreach (var image in images)
            {
                var point = isHorizontal ? new Point(offset, 0) : new Point(0, offset);
                canvas.Mutate(context => context.DrawImage(image, point, 1f));
                offset += (isHorizontal ? image.Width : image.Height) + spacing;
            }

            return canvas;
        }

        private static Image<Rgba32> CreateGridCollage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            var spacing = Math.Max(0, options.Spacing);
            var padding = Math.Max(0, options.Padding);
            var columns = Math.Max(1, Math.Min(images.Count, options.Columns));
            var rows = (int)Math.Ceiling(images.Count / (double)columns);
            var canvasWidth = Math.Max(1, options.CanvasWidth);
            var canvasHeight = Math.Max(1, options.CanvasHeight);
            var availableWidth = Math.Max(1, canvasWidth - padding * 2 - spacing * Math.Max(0, columns - 1));
            var availableHeight = Math.Max(1, canvasHeight - padding * 2 - spacing * Math.Max(0, rows - 1));
            var cellWidth = Math.Max(1, availableWidth / columns);
            var cellHeight = Math.Max(1, availableHeight / rows);
            var placements = new List<Tuple<Image<Rgba32>, Rectangle>>();

            for (var index = 0; index < images.Count; index++)
            {
                var column = index % columns;
                var row = index / columns;
                var x = padding + column * (cellWidth + spacing);
                var y = padding + row * (cellHeight + spacing);
                var width = column == columns - 1 ? Math.Max(1, canvasWidth - padding - x) : cellWidth;
                var height = row == rows - 1 ? Math.Max(1, canvasHeight - padding - y) : cellHeight;
                placements.Add(Tuple.Create(images[index], ClampPlacement(x, y, width, height, padding, canvasWidth, canvasHeight)));
            }

            var canvas = CreateCollageCanvas(canvasWidth, canvasHeight, options);
            DrawPlacements(canvas, placements, options.FitMode);
            return canvas;
        }

        private static Image<Rgba32> CreateWaterfallCollage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            var spacing = Math.Max(0, options.Spacing);
            var padding = Math.Max(0, options.Padding);
            var columns = Math.Max(1, Math.Min(images.Count, options.Columns));
            var canvasWidth = Math.Max(1, options.CanvasWidth);
            var columnWidth = Math.Max(1, (canvasWidth - padding * 2 - spacing * Math.Max(0, columns - 1)) / columns);
            var columnHeights = Enumerable.Repeat(padding, columns).ToArray();
            var placements = new List<Tuple<Image<Rgba32>, Rectangle>>();

            foreach (var image in images)
            {
                var column = IndexOfMin(columnHeights);
                var targetHeight = Math.Max(1, (int)Math.Round(image.Height * (columnWidth / (double)image.Width)));
                var x = padding + column * (columnWidth + spacing);
                var y = columnHeights[column];
                placements.Add(Tuple.Create(image, new Rectangle(x, y, columnWidth, targetHeight)));
                columnHeights[column] += targetHeight + spacing;
            }

            var canvasHeight = Math.Max(1, columnHeights.Max() - spacing + padding);
            var canvas = CreateCollageCanvas(canvasWidth, canvasHeight, options);
            DrawPlacements(canvas, placements, CollageFitMode.Cover);
            return canvas;
        }

        private static Image<Rgba32> CreatePosterCollage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            var spacing = Math.Max(0, options.Spacing);
            var padding = Math.Max(0, options.Padding);
            var canvasWidth = Math.Max(1, options.CanvasWidth);
            var canvasHeight = Math.Max(1, options.CanvasHeight);
            var availableWidth = Math.Max(1, canvasWidth - padding * 2);
            var availableHeight = Math.Max(1, canvasHeight - padding * 2);
            var heroHeight = images.Count == 1 ? availableHeight : Math.Max(1, (int)Math.Round((availableHeight - spacing) * 0.62d));
            var placements = new List<Tuple<Image<Rgba32>, Rectangle>>
            {
                Tuple.Create(images[0], new Rectangle(padding, padding, availableWidth, heroHeight))
            };

            var remaining = images.Count - 1;
            if (remaining > 0)
            {
                var bottomTop = padding + heroHeight + spacing;
                var bottomHeight = Math.Max(1, padding + availableHeight - bottomTop);
                var tileWidth = Math.Max(1, (availableWidth - spacing * Math.Max(0, remaining - 1)) / remaining);

                for (var i = 0; i < remaining; i++)
                {
                    var x = padding + i * (tileWidth + spacing);
                    var width = i == remaining - 1 ? padding + availableWidth - x : tileWidth;
                    placements.Add(Tuple.Create(images[i + 1], new Rectangle(x, bottomTop, Math.Max(1, width), bottomHeight)));
                }
            }

            var canvas = CreateCollageCanvas(canvasWidth, canvasHeight, options);
            DrawPlacements(canvas, placements, options.FitMode);
            return canvas;
        }

        private static Image<Rgba32> CreateFreeCollage(IReadOnlyList<Image<Rgba32>> images, StitchOptions options)
        {
            var spacing = Math.Max(0, options.Spacing);
            var padding = Math.Max(0, options.Padding);
            var canvasWidth = Math.Max(1, options.CanvasWidth);
            var canvasHeight = Math.Max(1, options.CanvasHeight);
            var usableWidth = Math.Max(1, canvasWidth - padding * 2);
            var usableHeight = Math.Max(1, canvasHeight - padding * 2);
            var placements = new List<Tuple<Image<Rgba32>, Rectangle>>();

            var templates = new[,]
            {
                { 0.02d, 0.04d, 0.58d, 0.52d },
                { 0.52d, 0.00d, 0.46d, 0.34d },
                { 0.08d, 0.58d, 0.42d, 0.38d },
                { 0.54d, 0.38d, 0.38d, 0.46d },
                { 0.32d, 0.28d, 0.34d, 0.38d },
                { 0.62d, 0.72d, 0.34d, 0.24d },
                { 0.00d, 0.26d, 0.30d, 0.28d },
                { 0.36d, 0.70d, 0.26d, 0.28d }
            };

            for (var index = 0; index < images.Count; index++)
            {
                var templateIndex = index % templates.GetLength(0);
                var cycle = index / templates.GetLength(0);
                var inset = cycle * Math.Max(0, spacing / 2);
                var x = padding + (int)Math.Round(templates[templateIndex, 0] * usableWidth) + inset;
                var y = padding + (int)Math.Round(templates[templateIndex, 1] * usableHeight) + inset;
                var width = (int)Math.Round(templates[templateIndex, 2] * usableWidth) - inset * 2;
                var height = (int)Math.Round(templates[templateIndex, 3] * usableHeight) - inset * 2;
                placements.Add(Tuple.Create(images[index], ClampPlacement(x, y, width, height, padding, canvasWidth, canvasHeight)));
            }

            var canvas = CreateCollageCanvas(canvasWidth, canvasHeight, options);
            DrawPlacements(canvas, placements, options.FitMode);
            return canvas;
        }

        private static Rectangle ClampPlacement(int x, int y, int width, int height, int padding, int canvasWidth, int canvasHeight)
        {
            var left = Math.Max(0, Math.Min(canvasWidth - 1, x));
            var top = Math.Max(0, Math.Min(canvasHeight - 1, y));
            var right = Math.Max(left + 1, Math.Min(canvasWidth - padding, left + Math.Max(1, width)));
            var bottom = Math.Max(top + 1, Math.Min(canvasHeight - padding, top + Math.Max(1, height)));
            return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static Image<Rgba32> CreateCollageCanvas(int width, int height, StitchOptions options)
        {
            return new Image<Rgba32>(Math.Max(1, width), Math.Max(1, height), ParseHexColor(options.BackgroundColor));
        }

        private static void DrawPlacements(Image<Rgba32> canvas, IEnumerable<Tuple<Image<Rgba32>, Rectangle>> placements, CollageFitMode fitMode)
        {
            foreach (var placement in placements)
            {
                using (var fitted = FitImageToRectangle(placement.Item1, placement.Item2.Width, placement.Item2.Height, fitMode))
                {
                    var x = placement.Item2.X;
                    var y = placement.Item2.Y;
                    if (fitMode == CollageFitMode.Contain)
                    {
                        x += Math.Max(0, (placement.Item2.Width - fitted.Width) / 2);
                        y += Math.Max(0, (placement.Item2.Height - fitted.Height) / 2);
                    }

                    canvas.Mutate(context => context.DrawImage(fitted, new Point(x, y), 1f));
                }
            }
        }

        private static Image<Rgba32> FitImageToRectangle(Image<Rgba32> source, int width, int height, CollageFitMode fitMode)
        {
            var targetWidth = Math.Max(1, width);
            var targetHeight = Math.Max(1, height);
            var targetRatio = targetWidth / (double)targetHeight;
            var sourceRatio = source.Width / (double)source.Height;
            var clone = source.Clone();

            if (fitMode == CollageFitMode.Contain)
            {
                clone.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Max
                }));
                return clone;
            }

            Rectangle crop;
            if (sourceRatio > targetRatio)
            {
                var cropWidth = Math.Max(1, (int)Math.Round(source.Height * targetRatio));
                crop = new Rectangle((source.Width - cropWidth) / 2, 0, cropWidth, source.Height);
            }
            else
            {
                var cropHeight = Math.Max(1, (int)Math.Round(source.Width / targetRatio));
                crop = new Rectangle(0, (source.Height - cropHeight) / 2, source.Width, cropHeight);
            }

            clone.Mutate(context => context.Crop(crop).Resize(targetWidth, targetHeight));
            return clone;
        }

        private static int IndexOfMin(IReadOnlyList<int> values)
        {
            var index = 0;
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] < values[index])
                {
                    index = i;
                }
            }

            return index;
        }

        private static void FlattenForPdf(Image<Rgba32> image)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        if (pixel.A == 255)
                        {
                            continue;
                        }

                        var alpha = pixel.A / 255d;
                        row[x] = new Rgba32(
                            (byte)Math.Round(pixel.R * alpha + 255 * (1d - alpha)),
                            (byte)Math.Round(pixel.G * alpha + 255 * (1d - alpha)),
                            (byte)Math.Round(pixel.B * alpha + 255 * (1d - alpha)),
                            255);
                    }
                }
            });
        }

        private static void WritePdf(string outputPath, IReadOnlyList<PdfPageImage> pages)
        {
            WritePdf(outputPath, pages.Count, index => pages[index]);
        }

        private static void WritePdf(string outputPath, int pageCount, Func<int, PdfPageImage> createPage)
        {
            var objectOffsets = new List<long> { 0 };
            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                WriteAscii(writer, "%PDF-1.4\n");
                WriteAscii(writer, "%\u00E2\u00E3\u00CF\u00D3\n");

                var catalogObject = 1;
                var pagesObject = 2;
                var firstPageObject = 3;
                var objectCount = 2 + pageCount * 3;

                WriteObject(writer, objectOffsets, catalogObject, $"<< /Type /Catalog /Pages {pagesObject} 0 R >>\n");

                var pageReferences = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{firstPageObject + i * 3} 0 R"));
                WriteObject(writer, objectOffsets, pagesObject, $"<< /Type /Pages /Kids [{pageReferences}] /Count {pageCount} >>\n");

                for (var i = 0; i < pageCount; i++)
                {
                    var page = createPage(i);
                    var pageObject = firstPageObject + i * 3;
                    var imageObject = pageObject + 1;
                    var contentObject = pageObject + 2;
                    var pageWidth = FormatPdfNumber(page.PageWidthPoints);
                    var pageHeight = FormatPdfNumber(page.PageHeightPoints);

                    WriteObject(writer, objectOffsets, pageObject,
                        $"<< /Type /Page /Parent {pagesObject} 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>\n");

                    WriteStreamObject(writer, objectOffsets, imageObject,
                        $"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.JpegBytes.Length} >>",
                        page.JpegBytes);

                    var content = Encoding.ASCII.GetBytes($"q\n{pageWidth} 0 0 {pageHeight} 0 0 cm\n/Im0 Do\nQ\n");
                    WriteStreamObject(writer, objectOffsets, contentObject, $"<< /Length {content.Length} >>", content);
                }

                var xrefOffset = stream.Position;
                WriteAscii(writer, $"xref\n0 {objectCount + 1}\n");
                WriteAscii(writer, "0000000000 65535 f \n");
                for (var i = 1; i <= objectCount; i++)
                {
                    WriteAscii(writer, objectOffsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                }

                WriteAscii(writer, $"trailer\n<< /Size {objectCount + 1} /Root {catalogObject} 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
            }
        }

        private static void WriteObject(BinaryWriter writer, IList<long> objectOffsets, int objectNumber, string body)
        {
            EnsureObjectOffsetSlot(objectOffsets, objectNumber);
            objectOffsets[objectNumber] = writer.BaseStream.Position;
            WriteAscii(writer, $"{objectNumber} 0 obj\n");
            WriteAscii(writer, body);
            WriteAscii(writer, "endobj\n");
        }

        private static void WriteStreamObject(BinaryWriter writer, IList<long> objectOffsets, int objectNumber, string dictionary, byte[] data)
        {
            EnsureObjectOffsetSlot(objectOffsets, objectNumber);
            objectOffsets[objectNumber] = writer.BaseStream.Position;
            WriteAscii(writer, $"{objectNumber} 0 obj\n");
            WriteAscii(writer, dictionary + "\nstream\n");
            writer.Write(data);
            WriteAscii(writer, "\nendstream\nendobj\n");
        }

        private static void EnsureObjectOffsetSlot(IList<long> objectOffsets, int objectNumber)
        {
            while (objectOffsets.Count <= objectNumber)
            {
                objectOffsets.Add(0);
            }
        }

        private static void WriteAscii(BinaryWriter writer, string text)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
        }

        private static string FormatPdfNumber(double value)
        {
            return Math.Max(1d, value).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static int ResolveStitchColumns(int imageCount, StitchOptions options)
        {
            switch (options.Mode)
            {
                case StitchMode.Horizontal:
                    return imageCount;
                case StitchMode.Grid:
                case StitchMode.Waterfall:
                    return Math.Max(1, Math.Min(imageCount, options.Columns));
                default:
                    return 1;
            }
        }

        private static void ApplyBackgroundProcessing(Image<Rgba32> image, ProcessingOptions options)
        {
            var backgroundColor = EstimateBackgroundColor(image);
            var replacementColor = ParseHexColor(options.BackgroundReplacementColor);
            var tolerance = Math.Max(0, Math.Min(255, options.BackgroundTolerance));
            var feather = Math.Max(0, Math.Min(100, options.BackgroundFeather));
            var softRange = Math.Max(1, feather);

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        var distance = ColorDistance(pixel, backgroundColor);

                        if (distance > tolerance + softRange)
                        {
                            continue;
                        }

                        var alphaFactor = distance <= tolerance ? 0d : (distance - tolerance) / softRange;
                        var alpha = (byte)Math.Round(pixel.A * alphaFactor);

                        if (options.BackgroundAction == BackgroundAction.ReplaceWithColor)
                        {
                            var blend = 1d - alphaFactor;
                            row[x] = new Rgba32(
                                BlendChannel(pixel.R, replacementColor.R, blend),
                                BlendChannel(pixel.G, replacementColor.G, blend),
                                BlendChannel(pixel.B, replacementColor.B, blend),
                                255);
                        }
                        else
                        {
                            row[x] = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                        }
                    }
                }
            });
        }

        private static Rgba32 EstimateBackgroundColor(Image<Rgba32> image)
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            long count = 0;
            var lastX = Math.Max(0, image.Width - 1);
            var lastY = Math.Max(0, image.Height - 1);
            var samplePoints = new[]
            {
                new Point(0, 0),
                new Point(lastX, 0),
                new Point(0, lastY),
                new Point(lastX, lastY)
            };

            foreach (var point in samplePoints)
            {
                var pixel = image[point.X, point.Y];
                red += pixel.R;
                green += pixel.G;
                blue += pixel.B;
                count++;
            }

            return new Rgba32((byte)(red / count), (byte)(green / count), (byte)(blue / count), 255);
        }

        private static double ColorDistance(Rgba32 pixel, Rgba32 target)
        {
            var red = pixel.R - target.R;
            var green = pixel.G - target.G;
            var blue = pixel.B - target.B;
            return Math.Sqrt(red * red + green * green + blue * blue);
        }

        private static byte BlendChannel(byte foreground, byte background, double backgroundWeight)
        {
            var value = foreground * (1d - backgroundWeight) + background * backgroundWeight;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
        }

        private static Rgba32 ParseHexColor(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            {
                throw new ArgumentException("背景替换色请使用 #RRGGBB 格式。");
            }

            return new Rgba32((byte)((number >> 16) & 255), (byte)((number >> 8) & 255), (byte)(number & 255), 255);
        }

        private static Image<Rgba32> CreateWatermarkImage(int imageWidth, int imageHeight, ProcessingOptions options)
        {
            byte[] pngBytes = null;
            Exception renderException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    pngBytes = RenderWatermarkPng(imageWidth, imageHeight, options);
                }
                catch (Exception ex)
                {
                    renderException = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (renderException != null)
            {
                throw renderException;
            }

            using (var stream = new MemoryStream(pngBytes))
            {
                return Image.Load<Rgba32>(stream);
            }
        }

        private static byte[] RenderWatermarkPng(int imageWidth, int imageHeight, ProcessingOptions options)
        {
            var text = options.WatermarkText.Trim();
            var fontSize = Math.Max(8, Math.Min(240, options.WatermarkFontSize));
            var opacity = Math.Max(1, Math.Min(100, options.WatermarkOpacity)) / 100d;
            var textBrush = new WpfSolidColorBrush(WpfColor.FromArgb((byte)Math.Round(255 * opacity), 255, 255, 255));
            var formattedText = CreateFormattedText(text, fontSize, textBrush);
            var margin = Math.Max(12, Math.Min(imageWidth, imageHeight) / 40d);
            var origin = ResolveWatermarkOrigin(options.WatermarkPosition, imageWidth, imageHeight, formattedText.Width, formattedText.Height, margin);
            var visual = new WpfDrawingVisual();

            using (var context = visual.RenderOpen())
            {
                var shadowBrush = new WpfSolidColorBrush(WpfColor.FromArgb((byte)Math.Round(180 * opacity), 15, 23, 42));
                context.DrawText(CreateFormattedText(text, fontSize, shadowBrush), new WpfPoint(origin.X + 2, origin.Y + 2));
                context.DrawText(formattedText, new WpfPoint(origin.X, origin.Y));
            }

            var bitmap = new WpfRenderTargetBitmap(imageWidth, imageHeight, 96, 96, WpfPixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new WpfPngBitmapEncoder();
            encoder.Frames.Add(WpfBitmapFrame.Create(bitmap));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static WpfFormattedText CreateFormattedText(string text, int fontSize, WpfSolidColorBrush brush)
        {
            return new WpfFormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new WpfTypeface("Segoe UI"),
                fontSize,
                brush,
                1.0);
        }

        private static WpfPoint ResolveWatermarkOrigin(WatermarkPosition position, int imageWidth, int imageHeight, double textWidth, double textHeight, double margin)
        {
            switch (position)
            {
                case WatermarkPosition.TopLeft:
                    return new WpfPoint(margin, margin);
                case WatermarkPosition.TopRight:
                    return new WpfPoint(Math.Max(margin, imageWidth - textWidth - margin), margin);
                case WatermarkPosition.Center:
                    return new WpfPoint(Math.Max(margin, (imageWidth - textWidth) / 2), Math.Max(margin, (imageHeight - textHeight) / 2));
                case WatermarkPosition.BottomLeft:
                    return new WpfPoint(margin, Math.Max(margin, imageHeight - textHeight - margin));
                default:
                    return new WpfPoint(Math.Max(margin, imageWidth - textWidth - margin), Math.Max(margin, imageHeight - textHeight - margin));
            }
        }

        private IImageEncoder CreateEncoder(OutputFormat format, ProcessingOptions options)
        {
            var quality = Math.Max(0, Math.Min(100, options.Quality));

            switch (format)
            {
                case OutputFormat.Jpeg:
                    return new JpegEncoder { Quality = quality };
                case OutputFormat.Png:
                    return new PngEncoder
                    {
                        CompressionLevel = PngCompressionLevel.BestCompression,
                        ColorType = options.QuantizePng ? PngColorType.Palette : PngColorType.RgbWithAlpha,
                        Quantizer = options.QuantizePng ? new SixLabors.ImageSharp.Processing.Processors.Quantization.OctreeQuantizer(new SixLabors.ImageSharp.Processing.Processors.Quantization.QuantizerOptions { MaxColors = options.PngColorCount }) : null
                    };
                case OutputFormat.Webp:
                    return new WebpEncoder
                    {
                        Quality = quality,
                        FileFormat = options.CompressionMode == CompressionMode.Lossless ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy
                    };
                case OutputFormat.Bmp:
                    return new BmpEncoder();
                case OutputFormat.Gif:
                    return new GifEncoder();
                case OutputFormat.Tiff:
                    return new TiffEncoder();
                default:
                    return new JpegEncoder { Quality = quality };
            }
        }

        private static BitmapImage CreateBitmapPreview(Image<Rgba32> source, int maxSide)
        {
            using (var preview = source.Clone())
            {
                if (preview.Width > maxSide || preview.Height > maxSide)
                {
                    preview.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Size = new Size(maxSide, maxSide),
                        Mode = ResizeMode.Max
                    }));
                }

                using (var stream = new MemoryStream())
                {
                    preview.Save(stream, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
                    stream.Position = 0;
                    return LoadBitmapFromStream(stream);
                }
            }
        }

        private static Image<Rgba32> LoadOrientedImage(string filePath)
        {
            IImageFormat format;
            return LoadOrientedImage(filePath, out format);
        }

        private static Image<Rgba32> LoadOrientedImage(string filePath, out IImageFormat format)
        {
            var image = Image.Load<Rgba32>(filePath, out format);
            image.Mutate(context => context.AutoOrient());
            image.Metadata.ExifProfile = null;
            return image;
        }

        private static BitmapImage LoadBitmapFromFile(string filePath, int decodePixelWidth)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapImage LoadBitmapFromStream(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static void ResizeForPreview(Image<Rgba32> image)
        {
            const int maxSide = 1200;
            if (image.Width <= maxSide && image.Height <= maxSide)
            {
                return;
            }

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxSide, maxSide),
                Mode = ResizeMode.Max
            }));
        }

        private static string NormalizeFormatName(string formatName, string extension)
        {
            if (!string.IsNullOrWhiteSpace(formatName))
            {
                return formatName.Equals("JPEG", StringComparison.OrdinalIgnoreCase) ? "JPEG" : formatName.ToUpperInvariant();
            }

            return FormatFromExtension(extension).ToString().ToUpperInvariant();
        }

        private static OutputFormat FormatFromName(string format, string filePath)
        {
            if (string.Equals(format, "JPEG", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "JPG", StringComparison.OrdinalIgnoreCase))
            {
                return OutputFormat.Jpeg;
            }

            if (Enum.TryParse(format, true, out OutputFormat parsed) && parsed != OutputFormat.KeepOriginal)
            {
                return parsed;
            }

            return FormatFromExtension(Path.GetExtension(filePath));
        }

        private static OutputFormat FormatFromExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return OutputFormat.Jpeg;
                case ".png":
                    return OutputFormat.Png;
                case ".webp":
                    return OutputFormat.Webp;
                case ".bmp":
                    return OutputFormat.Bmp;
                case ".gif":
                    return OutputFormat.Gif;
                case ".tif":
                case ".tiff":
                    return OutputFormat.Tiff;
                default:
                    return OutputFormat.Jpeg;
            }
        }

        private static string GetExtension(OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.Png:
                    return ".png";
                case OutputFormat.Webp:
                    return ".webp";
                case OutputFormat.Bmp:
                    return ".bmp";
                case OutputFormat.Gif:
                    return ".gif";
                case OutputFormat.Tiff:
                    return ".tiff";
                case OutputFormat.Jpeg:
                default:
                    return ".jpg";
            }
        }
    }
}