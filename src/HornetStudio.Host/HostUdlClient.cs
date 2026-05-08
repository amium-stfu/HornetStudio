using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Host;

public interface IHostUdlClient : IDisposable
{
    string Name { get; }
    string Host { get; }
    int Port { get; }
    bool IsConnected { get; }
    int LocalPort { get; }

    ItemDictionary Items { get; }

    event Action<uint, byte, byte[]>? FrameReceived;
    event Action<string>? Diagnostic;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed class HostUdlClient : IHostUdlClient
{
    private sealed class PendingCommand
    {
        public PendingCommand(double desiredValue, DateTime firstAttemptUtc)
        {
            DesiredValue = desiredValue;
            FirstAttemptUtc = firstAttemptUtc;
            LastSendUtc = DateTime.MinValue;
        }

        public double DesiredValue { get; set; }
        public DateTime FirstAttemptUtc { get; set; }
        public DateTime LastSendUtc { get; set; }
    }

    private readonly string _itemsPath;
    private readonly object _sync = new();
    private readonly Dictionary<uint, PendingCommand> _pendingCommands = new();
    private CancellationTokenSource? _lifetime;
    private CanHub? _hub;
    private long _receivedFrameLogCount;
    private long _ignoredFrameLogCount;
    private readonly IPEndPoint _remoteEndpoint;
    private Task? _heartbeatTask;
    private Task? _writebackTask;

    public HostUdlClient(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A client name is required.", nameof(name));
        }

        Name = name.Trim();
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;

        _itemsPath = $"runtime.UdlClient.{Name}";
        Items = new ItemDictionary(_itemsPath);

        _remoteEndpoint = ResolveRemoteEndpoint(Host, Port);
    }

    public string Name { get; }
    public string Host { get; }
    public int Port { get; }
    public bool IsConnected => _hub is not null;
    public int LocalPort => _hub?.LocalPort ?? 0;

    public ItemDictionary Items { get; }

    public event Action<uint, byte, byte[]>? FrameReceived;
    public event Action<string>? Diagnostic;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_hub is not null)
            {
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var lifetime = new CancellationTokenSource();
        _ = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);

        var hub = CanHubRegistry.GetOrCreate(Port);
        hub.FrameReceived += OnHubFrameReceived;
        hub.Diagnostic += OnHubDiagnostic;

        lock (_sync)
        {
            _lifetime = lifetime;
            _hub = hub;
        }

        RaiseDiagnostic($"open completed via CanHub localPort={hub.LocalPort} remote={Host}:{Port}");

        // Heartbeat/Time-Sync-Loop starten, damit das UDL-Gerät aktiv bleibt und Daten sendet.
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(lifetime.Token), lifetime.Token);

        // Writeback-Loop starten, um geaenderte Request-Werte (z.B. set/request)
        // in zyklische CAN-Write-PDOs umzusetzen.
        _writebackTask = Task.Run(() => WritebackLoopAsync(lifetime.Token), lifetime.Token);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CanHub? hub;
        CancellationTokenSource? lifetime;
        Task? heartbeatTask;
        Task? writebackTask;

        lock (_sync)
        {
            hub = _hub;
            lifetime = _lifetime;
            heartbeatTask = _heartbeatTask;
            writebackTask = _writebackTask;

            _hub = null;
            _lifetime = null;
            _heartbeatTask = null;
            _writebackTask = null;
            _pendingCommands.Clear();
        }

        lifetime?.Cancel();

        if (hub is not null)
        {
            hub.FrameReceived -= OnHubFrameReceived;
            hub.Diagnostic -= OnHubDiagnostic;
        }

        WaitForCompletion(heartbeatTask);
        WaitForCompletion(writebackTask);

        lifetime?.Dispose();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }

    private void RaiseDiagnostic(string message)
    {
        Diagnostic?.Invoke(message);
    }

    private void RaiseFrame(uint id, byte dlc, byte[] data)
    {
        FrameReceived?.Invoke(id, dlc, data);
    }

    private void OnHubFrameReceived(System.Net.EndPoint remoteEndpoint, uint id, byte dlc, byte[] data)
    {
        // Nur Frames vom konfigurierten Host weiterreichen.
        if (remoteEndpoint is System.Net.IPEndPoint ipEndpoint
            && !string.Equals(ipEndpoint.Address.ToString(), Host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var current = Interlocked.Increment(ref _receivedFrameLogCount);
        if (current <= 3 || current % 500 == 0)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] frame received from={remoteEndpoint} id=0x{id:X3} dlc={dlc} count={current}");
        }

        ProcessFrame(id, dlc, data);
        RaiseFrame(id, dlc, data);
    }

    private void OnHubDiagnostic(string message)
    {
        RaiseDiagnostic(message);
    }

    private void ProcessFrame(uint id, byte dlc, byte[] data)
    {
        if (data is null || dlc == 0)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] ignored empty payload id=0x{id:X3} dlc={dlc}");
            return;
        }

        if (id is >= 0x480 and <= 0x4FF)
        {
            HandleSubChannelPdo(id, dlc, data);
        }
        else if (id is >= 0x700 and <= 0x7FF)
        {
            // Heartbeats könnten hier später ausgewertet werden.
        }
        else
        {
            var current = System.Threading.Interlocked.Increment(ref _ignoredFrameLogCount);
            if (current <= 4 || current % 250 == 0)
            {
                RaiseDiagnostic($"[HostUdlClient:{Name}] frame ignored id=0x{id:X3} dlc={dlc}");
            }
        }
    }

    private void HandleSubChannelPdo(uint id, byte dlc, byte[] data)
    {
        if (dlc < 8 || data.Length < 8)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] subchannel ignored short frame id=0x{id:X3} dlc={dlc} len={data.Length}");
            return;
        }

        var moduleId = ((id & 0x7Fu) << 4) | ((uint)data[7] & 0x0Fu);
        var module = GetOrCreateModule(moduleId);

        var type = data[6];
        switch (type)
        {
            case 1:
            {
                var stateValue = Convert.ToInt32(Math.Round(BitConverter.ToSingle(data, 0), MidpointRounding.AwayFromZero));
                SetChannelReadValue(module.State, stateValue);
                TrackCommandState(moduleId, stateValue, module);
                break;
            }

            case 2:
                module.Alert.Value = BitConverter.ToSingle(data, 0);
                break;

            case 3:
            {
                var value = BitConverter.ToSingle(data, 0);
                SetChannelReadValue(module.Read, value);
                module.Value = value;
                var metadata = (ushort)(data[4] | (data[5] << 8));
                module.Read.Properties["MetaData"].Value = metadata;
                module.Properties["MetaData"].Value = metadata;
                break;
            }

            case 4:
                SetChannelReadValue(module.Set, BitConverter.ToSingle(data, 0));
                break;

            case 5:
                SetChannelReadValue(module.Out, BitConverter.ToSingle(data, 0));
                break;

            default:
                module.Properties["LastType"].Value = type;
                module.Properties["LastRaw"].Value = $"dlc={dlc}";
                break;
        }
    }

    private UdlModule GetOrCreateModule(uint moduleId)
    {
        var key = FormatModuleName(moduleId);
        if (Items.Has(key) && Items[key] is UdlModule existingModule)
        {
            existingModule.EnsureWriteMetadata();
            return existingModule;
        }

        RaiseDiagnostic($"[HostUdlClient:{Name}] create module {key} from moduleId=0x{moduleId:X3}");
        var module = new UdlModule(key, _itemsPath);
        module.Properties["module_id"].Value = moduleId;
        module.Properties["text"].Value = key;
        module.Properties["kind"].Value = "UdlModule";
        module.Properties["SendStatus"].Value = "idle";

        module.Read.Properties["text"].Value = $"{key} Read";
        module.Set.Properties["text"].Value = $"{key} Set";
        module.Out.Properties["text"].Value = $"{key} Out";
        module.State.Properties["text"].Value = $"{key} State";
        module.Alert.Properties["text"].Value = $"{key} Alert";

        module.Read.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.Set.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.Out.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.State.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.EnsureWriteMetadata();

        Items[key] = module;
        return module;
    }

    private static string FormatModuleName(uint moduleId)
        => $"m{moduleId:X3}";

    private static IPEndPoint ResolveRemoteEndpoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var addresses = Dns.GetHostAddresses(host);
        var selectedAddress = addresses.FirstOrDefault(static candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                              ?? addresses.FirstOrDefault()
                              ?? throw new SocketException((int)SocketError.HostNotFound);

        return new IPEndPoint(selectedAddress, port);
    }

    private async Task WritebackLoopAsync(CancellationToken token)
    {
        try
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] writeback loop started");
            while (!token.IsCancellationRequested)
            {
                foreach (var moduleId in GetPendingCommandModuleIds())
                {
                    if (!TryGetModule(moduleId, out var module))
                    {
                        continue;
                    }

                    TryWriteCommand(moduleId, module);
                }

                await Task.Delay(20, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] writeback loop error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var hub = _hub;
                if (hub is not null)
                {
                    // Heartbeat 0x70E
                    var hbPayload = new byte[] { 5, 4 };
                    hub.Transmit(_remoteEndpoint, 0x70E, (byte)hbPayload.Length, hbPayload);

                    // Zeit-Sync 0x100 (Unix-Millis, identisch zur alten Implementierung)
                    var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var bytes = BitConverter.GetBytes(milliseconds);
                    var timePayload = new byte[]
                    {
                        bytes[0],
                        bytes[1],
                        bytes[2],
                        bytes[3],
                        bytes[4],
                        bytes[5],
                        0x00,
                        0x08
                    };
                    hub.Transmit(_remoteEndpoint, 0x100, (byte)timePayload.Length, timePayload);
                }

                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Heartbeat-Fehler sollen die Verbindung nicht komplett abbrechen.
        }
    }

    private void OnWriteItemChanged(uint moduleId, UdlModule module, ItemChangedEventArgs e)
    {
        if (!IsWriteTriggerProperty(e.PropertyName))
        {
            return;
        }

        RaiseDiagnostic($"[HostUdlClient:{Name}] request changed moduleId=0x{moduleId:X3} item={e.Item.Path} parameter={e.PropertyName} value={FormatObject(TryGetWritePropertyValue(e.Item) ?? e.Item.Value)}");
        ProcessRequestWrite(moduleId, module, e.Item);
    }

    private void ProcessRequestWrite(uint moduleId, UdlModule module, ItemModel requestItem)
    {
        if (ReferenceEquals(requestItem, module.Read))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] process request write moduleId=0x{moduleId:X3} channel=read write={FormatObject(TryGetWritePropertyValue(module.Read))} read={FormatObject(module.Read.Value)}");
            TryWrite(moduleId, module.Read, module.Read, 3);
            return;
        }

        if (ReferenceEquals(requestItem, module.State))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] process request write moduleId=0x{moduleId:X3} channel=state write={FormatObject(TryGetWritePropertyValue(module.State))} state={FormatObject(module.State.Value)}");
            TryWriteCommand(moduleId, module);
            return;
        }

        if (ReferenceEquals(requestItem, module.Set))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] process request write moduleId=0x{moduleId:X3} channel=set write={FormatObject(TryGetWritePropertyValue(module.Set))} set={FormatObject(module.Set.Value)}");
            TryWrite(moduleId, module.Set, module.Set, 4);
            return;
        }

        if (ReferenceEquals(requestItem, module.Out))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] process request write moduleId=0x{moduleId:X3} channel=out write={FormatObject(TryGetWritePropertyValue(module.Out))} out={FormatObject(module.Out.Value)}");
            TryWrite(moduleId, module.Out, module.Out, 5);
            return;
        }

        RaiseDiagnostic($"[HostUdlClient:{Name}] process request write moduleId=0x{moduleId:X3} channel=unknown requestPath={requestItem.Path}");
    }

    private void TryWriteCommand(uint moduleId, UdlModule module)
    {
        if (!TryGetCommandRequest(moduleId, module, out var desiredValue, out var shouldSend, out var timedOut))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] state write skipped moduleId=0x{moduleId:X3} reason=no-request state={FormatObject(module.State.Value)} request={FormatObject(TryGetWritePropertyValue(module.State))}");
            return;
        }

        if (timedOut)
        {
            module.Properties["SendStatus"].Value = "timeout";
            ClearRequestedValue(module.State);
            RaiseDiagnostic($"[HostUdlClient:{Name}] state timeout moduleId=0x{moduleId:X3} desired={desiredValue:0.###}");
            return;
        }

        if (!shouldSend)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] state write deferred moduleId=0x{moduleId:X3} desired={desiredValue:0.###}");
            return;
        }

        RaiseDiagnostic($"[HostUdlClient:{Name}] state write request moduleId=0x{moduleId:X3} desired={desiredValue:0.###} source={module.State.Name}");
        module.Properties["SendStatus"].Value = "sending";
        var queued = SendWritePdo(moduleId, desiredValue, 1);
        RaiseDiagnostic($"[HostUdlClient:{Name}] state write send result moduleId=0x{moduleId:X3} desired={desiredValue:0.###} queued={queued}");
        if (queued)
        {
            lock (_sync)
            {
                if (_pendingCommands.TryGetValue(moduleId, out var pending))
                {
                    pending.DesiredValue = desiredValue;
                    pending.LastSendUtc = DateTime.UtcNow;
                }
                else
                {
                    var pendingCommand = new PendingCommand(desiredValue, DateTime.UtcNow)
                    {
                        LastSendUtc = DateTime.UtcNow,
                    };
                    _pendingCommands[moduleId] = pendingCommand;
                }
            }

            ClearRequestedValue(module.State);
        }
    }

    private bool TryGetCommandRequest(uint moduleId, UdlModule module, out double desiredValue, out bool shouldSend, out bool timedOut)
    {
        desiredValue = 0;
        shouldSend = false;
        timedOut = false;

        var hasRequest = TryGetRequestedValue(module.State, out var requestedValue);
        var now = DateTime.UtcNow;
        var sendTimeout = GetSendTimeout();

        lock (_sync)
        {
            if (hasRequest)
            {
                desiredValue = requestedValue;
                if (!_pendingCommands.TryGetValue(moduleId, out var pending)
                    || Math.Abs(pending.DesiredValue - desiredValue) > 0.0001)
                {
                    _pendingCommands[moduleId] = new PendingCommand(desiredValue, now);
                    shouldSend = true;
                    return true;
                }

                if (now - pending.FirstAttemptUtc >= sendTimeout)
                {
                    _pendingCommands.Remove(moduleId);
                    timedOut = true;
                    return true;
                }

                if (pending.LastSendUtc == DateTime.MinValue || now - pending.LastSendUtc >= TimeSpan.FromMilliseconds(20))
                {
                    shouldSend = true;
                }

                return true;
            }

            if (_pendingCommands.TryGetValue(moduleId, out var existingPending))
            {
                desiredValue = existingPending.DesiredValue;
                if (now - existingPending.FirstAttemptUtc >= sendTimeout)
                {
                    _pendingCommands.Remove(moduleId);
                    timedOut = true;
                    return true;
                }

                shouldSend = existingPending.LastSendUtc == DateTime.MinValue || now - existingPending.LastSendUtc >= TimeSpan.FromMilliseconds(20);
                return true;
            }
        }

        return false;
    }

    private TimeSpan GetSendTimeout()
    {
        // Mindest-Timeout 20ms, analog zur alten Implementierung
        return TimeSpan.FromMilliseconds(Math.Max(20, 250));
    }

    private void TrackCommandState(uint moduleId, double stateValue, UdlModule module)
    {
        var acknowledged = false;

        lock (_sync)
        {
            if (!_pendingCommands.TryGetValue(moduleId, out var pending))
            {
                return;
            }

            if (DateTime.UtcNow <= pending.LastSendUtc)
            {
                return;
            }

            if (Math.Abs(pending.DesiredValue - stateValue) > 0.0001)
            {
                return;
            }

            _pendingCommands.Remove(moduleId);
            acknowledged = true;
        }

        if (!acknowledged)
        {
            return;
        }

        ClearRequestedValue(module.State);
        module.Properties["SendStatus"].Value = "ok";
        RaiseDiagnostic($"[HostUdlClient:{Name}] state acknowledged moduleId=0x{moduleId:X3} value={stateValue:0.###}");
    }

    private void TryWrite(uint moduleId, ItemModel requestItem, ItemModel currentItem, int function)
    {
        RaiseDiagnostic($"[HostUdlClient:{Name}] try write moduleId=0x{moduleId:X3} function={function} requestPath={requestItem.Path} requestValue={FormatObject(TryGetWritePropertyValue(requestItem) ?? requestItem.Value)} currentPath={currentItem.Path} currentValue={FormatObject(currentItem.Value)}");

        if (!TryGetDesiredWriteValue(requestItem, currentItem, out var desiredValue))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] try write skipped moduleId=0x{moduleId:X3} function={function} reason=no-desired-value requestPath={requestItem.Path}");
            return;
        }

        if (!TryConvertToDouble(currentItem.Value, out double currentValue))
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] write request moduleId=0x{moduleId:X3} function={function} current=<unset> desired={desiredValue:0.###} source={requestItem.Path}");
            var queuedWithoutCurrent = SendWritePdo(moduleId, desiredValue, function);
            RaiseDiagnostic($"[HostUdlClient:{Name}] write send result moduleId=0x{moduleId:X3} function={function} queued={queuedWithoutCurrent} source={requestItem.Path}");
            return;
        }

        if (Math.Abs(desiredValue - currentValue) <= 0.0001)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] try write skipped moduleId=0x{moduleId:X3} function={function} reason=desired-equals-current current={currentValue:0.###} desired={desiredValue:0.###} source={requestItem.Path}");
            return;
        }

        RaiseDiagnostic($"[HostUdlClient:{Name}] write request moduleId=0x{moduleId:X3} function={function} current={currentValue:0.###} desired={desiredValue:0.###} source={requestItem.Path}");
        var queued = SendWritePdo(moduleId, desiredValue, function);
        RaiseDiagnostic($"[HostUdlClient:{Name}] write send result moduleId=0x{moduleId:X3} function={function} queued={queued} source={requestItem.Path}");
    }

    private bool SendWritePdo(uint moduleId, double value, int function)
    {
        var hub = _hub;
        if (hub is null)
        {
            RaiseDiagnostic($"[HostUdlClient:{Name}] send write pdo skipped moduleId=0x{moduleId:X3} function={function} reason=no-hub value={value:0.###}");
            return false;
        }

        var writeId = GetWriteIdFromModule(moduleId);
        var data = new byte[8];

        Array.Copy(BitConverter.GetBytes((float)value), 0, data, 0, 4);
        data[4] = 0;
        data[5] = 0;
        data[6] = (byte)function;
        data[7] = (byte)(moduleId & 0x0F);

        RaiseDiagnostic($"[HostUdlClient:{Name}] send write pdo id=0x{writeId:X3} function={function} moduleId=0x{moduleId:X3} data={FormatBytes(data, 8)}");
        hub.Transmit(_remoteEndpoint, writeId, (byte)data.Length, data);
        return true;
    }

    private static uint GetWriteIdFromModule(uint moduleId)
    {
        var baseId = (moduleId >> 4) & 0x7F;
        return 0x500 | baseId;
    }

    private uint[] GetPendingCommandModuleIds()
    {
        lock (_sync)
        {
            return _pendingCommands.Keys.ToArray();
        }
    }

    private bool TryGetModule(uint moduleId, out UdlModule module)
    {
        var key = FormatModuleName(moduleId);
        if (Items.Has(key) && Items[key] is UdlModule existingModule)
        {
            module = existingModule;
            return true;
        }

        module = null!;
        return false;
    }

    private static bool TryGetWriteValue(ItemModel item, out double value)
    {
        value = 0;
        if (item.Properties.Has("write"))
        {
            return TryConvertToDouble(item.Properties["write"].Value, out value) && !double.IsNaN(value);
        }

        if (!item.Properties.Has("Write"))
        {
            return false;
        }

        return TryConvertToDouble(item.Properties["Write"].Value, out value) && !double.IsNaN(value);
    }

    private static bool TryGetSetValue(ItemModel item, out double value)
    {
        value = 0;
        if (item.Properties.Has("set"))
        {
            return TryConvertToDouble(item.Properties["set"].Value, out value) && !double.IsNaN(value);
        }

        if (!item.Properties.Has("Set"))
        {
            return false;
        }

        return TryConvertToDouble(item.Properties["Set"].Value, out value) && !double.IsNaN(value);
    }

    private static bool TryGetRequestItemValue(ItemModel item, out double value)
    {
        value = 0;
        if (!item.Has("Request"))
        {
            return false;
        }

        return TryConvertToDouble(item["Request"].Value, out value) && !double.IsNaN(value);
    }

    private static bool TryGetRequestedValue(ItemModel item, out double value)
    {
        return TryGetRequestItemValue(item, out value)
            || TryGetSetValue(item, out value)
            || TryGetWriteValue(item, out value);
    }

    private static bool TryGetDesiredWriteValue(ItemModel requestItem, ItemModel ownerItem, out double value)
    {
        value = 0;

        if (TryGetRequestedValue(requestItem, out value))
        {
            return true;
        }

        if (TryConvertToDouble(requestItem.Value, out value) && !double.IsNaN(value))
        {
            return true;
        }

        return TryGetSetValue(ownerItem, out value)
            || TryGetWriteValue(ownerItem, out value);
    }

    private static void ClearRequestedValue(ItemModel item)
    {
        if (item.Properties.Has("write"))
        {
            item.Properties["write"].Value = item.Properties.Has("read")
                ? item.Properties["read"].Value
                : null!;
        }

        if (item.Has("Request"))
        {
            if (item.Value is null)
            {
                item["Request"].Properties.Remove("Value");
            }
            else
            {
                item["Request"].Value = item.Value;
            }

            item["Request"].Properties.Remove("Set");
            item["Request"].Properties.Remove("Write");
        }

        item.Properties.Remove("Set");
        item.Properties.Remove("Write");
        item.Properties.Remove("set");
    }

    private static bool IsWriteTriggerProperty(string propertyName)
        => string.Equals(propertyName, "write", StringComparison.OrdinalIgnoreCase)
           || string.Equals(propertyName, "set", StringComparison.OrdinalIgnoreCase);

    private static object? TryGetWritePropertyValue(ItemModel item)
        => item.Properties.Has("write")
            ? item.Properties["write"].Value
            : (item.Properties.Has("Write") ? item.Properties["Write"].Value : null);

    private static void SetChannelReadValue(ItemModel item, object? value)
    {
        item.Properties["read"].Value = value!;
    }

    private static bool TryConvertToDouble(object? value, out double converted)
    {
        converted = 0;
        if (value is null)
        {
            return false;
        }

        switch (value)
        {
            case double doubleValue:
                converted = doubleValue;
                return true;
            case float floatValue:
                converted = floatValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed):
                converted = parsed;
                return true;
            default:
                try
                {
                    converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
        }
    }

    private static string FormatBytes(byte[] data, byte dlc)
    {
        if (dlc == 0 || data.Length == 0)
        {
            return "<empty>";
        }

        var length = Math.Min(data.Length, dlc);
        var parts = new string[length];
        for (var index = 0; index < length; index++)
        {
            parts[index] = data[index].ToString("X2", CultureInfo.InvariantCulture);
        }

        return string.Join(" ", parts);
    }

    private static string FormatObject(object? value)
        => value is null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";

    private static void WaitForCompletion(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.Wait(250);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(static ex => ex is OperationCanceledException))
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}
