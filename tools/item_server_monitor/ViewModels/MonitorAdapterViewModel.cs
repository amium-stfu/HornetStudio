using Avalonia.Threading;
using Item.Server.Monitor.Hosting;

namespace Item.Server.Monitor.ViewModels;

public sealed class MonitorAdapterViewModel : ObservableObject, IDisposable
{
    private readonly MonitorHost _monitorHost;
    private readonly MonitorAdapterRuntime _runtime;
    private readonly RelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;
    private string _host;
    private string _portText;
    private string _baseTopic;
    private string _validationError = string.Empty;
    private bool _disposed;

    internal MonitorAdapterViewModel(MonitorHost monitorHost, MonitorAdapterRuntime runtime)
    {
        _monitorHost = monitorHost ?? throw new ArgumentNullException(nameof(monitorHost));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _host = runtime.Options.Host;
        _portText = runtime.Options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _baseTopic = runtime.Options.BaseTopic;
        _startCommand = new RelayCommand(Start, CanStart);
        _stopCommand = new RelayCommand(Stop, CanStop);
        _runtime.StateChanged += OnRuntimeStateChanged;
        ValidateInputs(applyWhenValid: true);
    }

    public string DisplayName => _runtime.Definition.DisplayName;

    public string Description => _runtime.Definition.Description;

    public RelayCommand StartCommand => _startCommand;

    public RelayCommand StopCommand => _stopCommand;

    public string Host
    {
        get => _host;
        set
        {
            if (!SetProperty(ref _host, value))
            {
                return;
            }

            ValidateInputs(applyWhenValid: CanEditOptions);
        }
    }

    public string PortText
    {
        get => _portText;
        set
        {
            if (!SetProperty(ref _portText, value))
            {
                return;
            }

            ValidateInputs(applyWhenValid: CanEditOptions);
        }
    }

    public string BaseTopic
    {
        get => _baseTopic;
        set
        {
            if (!SetProperty(ref _baseTopic, value))
            {
                return;
            }

            ValidateInputs(applyWhenValid: CanEditOptions);
        }
    }

    public string StatusText => _runtime.Status switch
    {
        MonitorAdapterStatus.Stopped => "Stopped",
        MonitorAdapterStatus.Starting => "Starting",
        MonitorAdapterStatus.Running => "Running",
        MonitorAdapterStatus.Stopping => "Stopping",
        MonitorAdapterStatus.Failed => "Failed",
        _ => _runtime.Status.ToString(),
    };

    public string StatusBrush => _runtime.Status switch
    {
        MonitorAdapterStatus.Running => "#065F46",
        MonitorAdapterStatus.Starting or MonitorAdapterStatus.Stopping => "#9A3412",
        MonitorAdapterStatus.Failed => "#991B1B",
        _ => "#475569",
    };

    public string Endpoint => TryBuildOptions(out var options, out _) ? _runtime.Definition.Factory.FormatEndpoint(options) : "<invalid>";

    public bool CanEditOptions => _runtime.Status is MonitorAdapterStatus.Stopped or MonitorAdapterStatus.Failed;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string ErrorText => string.IsNullOrWhiteSpace(_validationError) ? _runtime.LastError : _validationError;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.StateChanged -= OnRuntimeStateChanged;
    }

    private bool CanStart() => !_runtime.IsBusy && _runtime.Status != MonitorAdapterStatus.Running && TryBuildOptions(out _, out _);

    private bool CanStop() => !_runtime.IsBusy && _runtime.Status == MonitorAdapterStatus.Running;

    private async void Start()
    {
        if (!TryBuildOptions(out var options, out var validationError))
        {
            _validationError = validationError;
            RefreshState();
            return;
        }

        _validationError = string.Empty;
        _runtime.ApplyOptions(options);
        RefreshState();
        await _monitorHost.StartAdapterAsync(_runtime.Definition.Id).ConfigureAwait(false);
    }

    private async void Stop()
    {
        _validationError = string.Empty;
        RefreshState();
        await _monitorHost.StopAdapterAsync(_runtime.Definition.Id).ConfigureAwait(false);
    }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(RefreshState);

    private void RefreshState()
    {
        if (_disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(CanEditOptions));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorText));
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
    }

    private void ValidateInputs(bool applyWhenValid)
    {
        if (TryBuildOptions(out var options, out var validationError))
        {
            _validationError = string.Empty;
            if (applyWhenValid)
            {
                _runtime.ApplyOptions(options);
            }
        }
        else
        {
            _validationError = validationError;
        }

        RefreshState();
    }

    private bool TryBuildOptions(out MonitorAdapterOptions options, out string validationError)
    {
        options = new MonitorAdapterOptions();
        var trimmedHost = (Host ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedHost))
        {
            validationError = "Host is required.";
            return false;
        }

        if (!int.TryParse(PortText, out var port) || port < 1 || port > 65535)
        {
            validationError = "Port must be between 1 and 65535.";
            return false;
        }

        options = new MonitorAdapterOptions
        {
            Host = trimmedHost,
            Port = port,
            BaseTopic = (BaseTopic ?? string.Empty).Trim(),
        };
        validationError = string.Empty;
        return true;
    }
}