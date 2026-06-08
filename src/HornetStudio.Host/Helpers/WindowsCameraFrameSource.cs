using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using AForge.Video;
using AForge.Video.DirectShow;
using HornetStudio.Host.Registries;
using HornetStudio.Logging;

namespace HornetStudio.Host.Helpers;

/// <summary>
/// Provides frames from a Windows video input device.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCameraFrameSource : ICameraFrameSource, IDisposable
{
    private readonly int _deviceIndex;
    private readonly object _sync = new();
    private readonly List<string> _supportedResolutions = new();
    private VideoCaptureDevice? _device;
    private EventHandler? _frameAvailable;
    private byte[]? _currentFrame;
    private bool _disposed;
    private bool _isStarting;
    private int _subscriberCount;
    private string? _currentResolutionLabel;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsCameraFrameSource"/> class.
    /// </summary>
    /// <param name="name">The display name of the camera source.</param>
    /// <param name="deviceIndex">The zero-based video input device index.</param>
    public WindowsCameraFrameSource(string name, int deviceIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Camera name must not be empty.", nameof(name));
        }

        Name = name.Trim();
        _deviceIndex = deviceIndex;

        LoadSupportedResolutions();
    }

    /// <summary>
    /// Gets the camera source name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the latest captured frame as encoded image bytes.
    /// </summary>
    public object? CurrentFrame
    {
        get
        {
            lock (_sync)
            {
                return _currentFrame;
            }
        }
    }

    /// <summary>
    /// Gets the supported video resolutions reported by the camera device.
    /// </summary>
    public IReadOnlyCollection<string> SupportedResolutions
    {
        get
        {
            lock (_sync)
            {
                return _supportedResolutions.ToArray();
            }
        }
    }

    /// <summary>
    /// Sets the desired camera resolution.
    /// </summary>
    /// <param name="resolutionLabel">The resolution label in WIDTHxHEIGHT format.</param>
    public void SetResolution(string? resolutionLabel)
    {
        if (_disposed || string.IsNullOrWhiteSpace(resolutionLabel))
        {
            return;
        }

        bool shouldRestart;
        lock (_sync)
        {
            _currentResolutionLabel = resolutionLabel.Trim();
            shouldRestart = _device is not null || _isStarting;
        }

        if (shouldRestart)
        {
            RestartDeviceWithResolution();
        }
    }

    /// <summary>
    /// Occurs when a new frame is available.
    /// </summary>
    public event EventHandler? FrameAvailable
    {
        add
        {
            if (value is null)
            {
                return;
            }

            var shouldStart = false;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _frameAvailable += value;
                _subscriberCount++;
                shouldStart = _subscriberCount == 1 && _device is null && !_isStarting;
            }

            if (shouldStart)
            {
                StartDevice();
            }
        }

        remove
        {
            if (value is null)
            {
                return;
            }

            var shouldStop = false;
            lock (_sync)
            {
                _frameAvailable -= value;
                if (_subscriberCount > 0)
                {
                    _subscriberCount--;
                }

                shouldStop = _subscriberCount == 0;
            }

            if (shouldStop)
            {
                StopDevice();
            }
        }
    }

    /// <summary>
    /// Stops the camera device and releases associated resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            _frameAvailable = null;
            _subscriberCount = 0;
        }

        StopDevice();
    }

    private void LoadSupportedResolutions()
    {
        try
        {
            var device = CreateVideoCaptureDevice();
            if (device is null)
            {
                return;
            }

            UpdateSupportedResolutions(device.VideoCapabilities ?? Array.Empty<VideoCapabilities>());
        }
        catch (Exception ex)
        {
            HostLogger.Log.Warning(ex, "[Cameras] Failed to read supported resolutions for camera '{Name}'.", Name);
        }
    }

    private void StartDevice()
    {
        lock (_sync)
        {
            if (_disposed || _device is not null || _isStarting)
            {
                return;
            }

            _isStarting = true;
        }

        try
        {
            var device = CreateVideoCaptureDevice();
            if (device is null)
            {
                return;
            }

            var capabilities = device.VideoCapabilities ?? Array.Empty<VideoCapabilities>();
            UpdateSupportedResolutions(capabilities);

            var preferred = SelectPreferredCapabilities(capabilities);
            if (preferred is not null)
            {
                device.VideoResolution = preferred;
            }

            device.NewFrame += OnNewFrame;
            device.Start();

            lock (_sync)
            {
                if (_disposed || _subscriberCount == 0)
                {
                    StopDevice(device);
                    return;
                }

                _device = device;
            }
        }
        catch (Exception ex)
        {
            HostLogger.Log.Warning(ex, "[Cameras] Failed to start camera '{Name}'.", Name);
        }
        finally
        {
            lock (_sync)
            {
                _isStarting = false;
            }
        }
    }

    private VideoCaptureDevice? CreateVideoCaptureDevice()
    {
        var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        if (devices.Count == 0 || _deviceIndex < 0 || _deviceIndex >= devices.Count)
        {
            return null;
        }

        var info = devices[_deviceIndex];
        return new VideoCaptureDevice(info.MonikerString);
    }

    private void UpdateSupportedResolutions(VideoCapabilities[] capabilities)
    {
        var resolutionLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cap in capabilities)
        {
            resolutionLabels.Add($"{cap.FrameSize.Width}x{cap.FrameSize.Height}");
        }

        lock (_sync)
        {
            _supportedResolutions.Clear();
            _supportedResolutions.AddRange(resolutionLabels.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }
    }

    private VideoCapabilities? SelectPreferredCapabilities(VideoCapabilities[] capabilities)
    {
        if (capabilities.Length == 0)
        {
            return null;
        }

        string? desiredLabel;
        lock (_sync)
        {
            desiredLabel = _currentResolutionLabel;
        }

        if (!string.IsNullOrWhiteSpace(desiredLabel))
        {
            var match = capabilities.FirstOrDefault(capability =>
                string.Equals($"{capability.FrameSize.Width}x{capability.FrameSize.Height}", desiredLabel, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        var hd = capabilities.FirstOrDefault(capability => capability.FrameSize.Width == 1280 && capability.FrameSize.Height == 720);
        return hd ?? capabilities[0];
    }

    private void RestartDeviceWithResolution()
    {
        VideoCaptureDevice? oldDevice;
        lock (_sync)
        {
            if (_disposed || _subscriberCount == 0)
            {
                return;
            }

            oldDevice = _device;
            _device = null;
        }

        StopDevice(oldDevice);
        StartDevice();
    }

    private void StopDevice()
    {
        VideoCaptureDevice? oldDevice;
        lock (_sync)
        {
            oldDevice = _device;
            _device = null;
            _currentFrame = null;
        }

        StopDevice(oldDevice);
    }

    private void StopDevice(VideoCaptureDevice? device)
    {
        if (device is null)
        {
            return;
        }

        try
        {
            device.NewFrame -= OnNewFrame;
            if (device.IsRunning)
            {
                device.SignalToStop();
                device.WaitForStop();
            }
        }
        catch (Exception ex)
        {
            HostLogger.Log.Warning(ex, "[Cameras] Failed to stop camera '{Name}'.", Name);
        }
    }

    private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            using Bitmap frame = (Bitmap)eventArgs.Frame.Clone();
            using var ms = new MemoryStream();
            frame.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();

            if (bytes.Length == 0)
            {
                return;
            }

            lock (_sync)
            {
                _currentFrame = bytes;
            }

            _frameAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HostLogger.Log.Debug(ex, "[Cameras] Failed to process frame for camera '{Name}'.", Name);
        }
    }
}
