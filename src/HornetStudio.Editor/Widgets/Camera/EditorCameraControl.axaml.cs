using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Interactivity;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Controls;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Widgets;

public partial class EditorCameraControl : EditorTemplateWidget
{
    public static readonly StyledProperty<Avalonia.Media.Imaging.Bitmap?> CurrentFrameImageProperty =
        AvaloniaProperty.Register<EditorCameraControl, Avalonia.Media.Imaging.Bitmap?>(nameof(CurrentFrameImage));

    private ICameraFrameSource? _currentCamera;
    private Avalonia.Media.Imaging.Bitmap? _currentBitmap;
    private FolderItemModel? _currentModel;

    public EditorCameraControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public Avalonia.Media.Imaging.Bitmap? CurrentFrameImage
    {
        get => GetValue(CurrentFrameImageProperty);
        private set => SetValue(CurrentFrameImageProperty, value);
    }

    private FolderItemModel? Model => DataContext as FolderItemModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(Model, _currentModel))
        {
            if (_currentModel is System.ComponentModel.INotifyPropertyChanged oldNotify)
            {
                oldNotify.PropertyChanged -= OnModelPropertyChanged;
            }

            _currentModel = Model;

            if (_currentModel is System.ComponentModel.INotifyPropertyChanged newNotify)
            {
                newNotify.PropertyChanged += OnModelPropertyChanged;
            }
        }

        UpdateCameraSubscription();
    }

    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            UpdateCameraSubscription();
            ApplyCameraResolution();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(FolderItemModel.Name), StringComparison.Ordinal))
        {
            var model = Model;
            if (model is not null)
            {
                model.ControlCaption = model.Name;
            }

            return;
        }

        if (string.Equals(e.PropertyName, nameof(FolderItemModel.CameraName), StringComparison.Ordinal))
        {
            UpdateCameraSubscription();
            ApplyCameraResolution();
            var model = Model;
            if (model is not null)
            {
                model.Footer = model.CameraName;
            }
            return;
        }

        if (string.Equals(e.PropertyName, nameof(FolderItemModel.CameraResolution), StringComparison.Ordinal))
        {
            ApplyCameraResolution();
        }
    }

    private void UpdateCameraSubscription()
    {
        if (_currentCamera != null)
        {
            _currentCamera.FrameAvailable -= OnFrameAvailable;
            _currentCamera = null;
        }

        var model = Model;
        if (model == null)
            return;

        if (string.IsNullOrWhiteSpace(model.CameraName))
            return;

        if (HostRegistries.Cameras.TryGet(model.CameraName, out var source) && source is not null)
        {
            _currentCamera = source;
            _currentCamera.FrameAvailable += OnFrameAvailable;
        }
    }

    private void ApplyCameraResolution()
    {
        var model = Model;
        if (model == null)
        {
            return;
        }

        var camera = _currentCamera;
        if (camera == null)
        {
            return;
        }

        try
        {
            camera.SetResolution(model.CameraResolution);
        }
        catch
        {
            // Resolution-Wechsel ist optional; Fehler hier ignorieren
        }
    }

    private void OnFrameAvailable(object? sender, EventArgs e)
    {
        var frame = _currentCamera?.CurrentFrame;
        if (frame is not byte[] bytes || bytes.Length == 0)
        {
            return;
        }

        void UpdateImage()
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);

                var old = _currentBitmap;
                _currentBitmap = bitmap;
                CurrentFrameImage = bitmap;
                old?.Dispose();
            }
            catch
            {
                // ignore rendering errors
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateImage();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateImage);
        }
    }

    private async void OnSnapshotClicked(object? sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model == null)
            return;

        try
        {
            var directory = model.CsvDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CameraSnapshots");
            }

            Directory.CreateDirectory(directory);

            var baseName = string.IsNullOrWhiteSpace(model.CsvFilename) ? "snapshot" : model.CsvFilename;
            var timestamp = model.CsvAddTimestamp ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : null;
            var fileName = timestamp == null
                ? baseName + ".jpg"
                : $"{baseName}_{timestamp}.jpg";

            var fullPath = Path.Combine(directory, fileName);

            if (_currentCamera != null && _currentCamera.CurrentFrame is { } frame)
            {
                var overlayText = string.IsNullOrWhiteSpace(model.CameraOverlayText)
                    ? model.Name ?? string.Empty
                    : model.CameraOverlayText;

                await SaveFrameAsync(frame, fullPath, overlayText);
            }
        }
        catch
        {
            // Swallow errors for now – can be improved with logging.
        }
    }

    private Task SaveFrameAsync(object frame, string fullPath, string overlayText)
    {
        try
        {
            if (frame is byte[] bytes && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                using var sourceImage = Image.FromStream(ms);
                using var composed = new System.Drawing.Bitmap(sourceImage.Width, sourceImage.Height);
                using (var g = Graphics.FromImage(composed))
                {
                    g.DrawImage(sourceImage, 0, 0, sourceImage.Width, sourceImage.Height);

                    if (!string.IsNullOrWhiteSpace(overlayText))
                    {
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var text = overlayText + " " + timestamp;

                        using var font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
                        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Lime);
                        var point = new System.Drawing.PointF(8, 8);
                        g.DrawString(text, font, brush, point);
                    }
                }

                ImageCodecInfo? jpegEncoder = null;
                foreach (var codec in ImageCodecInfo.GetImageEncoders())
                {
                    if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    {
                        jpegEncoder = codec;
                        break;
                    }
                }

                if (jpegEncoder is not null)
                {
                    using var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                    composed.Save(fullPath, jpegEncoder, encoderParams);
                }
                else
                {
                    composed.Save(fullPath, ImageFormat.Jpeg);
                }
            }
            else
            {
                // Fallback: create empty file so at least something is visible on Disk
                if (!File.Exists(fullPath))
                {
                    using (File.Create(fullPath)) { }
                }
            }
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model == null)
            return;

        var directory = model.CsvDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            directory = directory.Trim();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    private void OnInteractivePointerPressed(object? sender, RoutedEventArgs e)
    {
        // Prevent parent editors from treating header button clicks as drag operations.
        e.Handled = true;
    }
}

