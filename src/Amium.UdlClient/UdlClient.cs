using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ItemModel = Amium.Items.Item;
using ItemChangedEventArgs = Amium.Items.ItemChangedEventArgs;
using ItemDictionary = Amium.Items.ItemDictionary;

namespace Amium.UdlClient;

public sealed class UdlClient : IDisposable
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
    private Task? _heartbeatTask;
    private Task? _writebackTask;
    private Can? _can;
    private DateTime _nextHeartbeatUtc = DateTime.UtcNow;
    private long _rxDispatchLogCount;
    private long _ignoredFrameLogCount;
    private long _unknownTypeLogCount;

    public UdlClient(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A client name is required.", nameof(name));
        }

        Name = name.Trim();
        _itemsPath = $"Runtime/UdlClient/{Name}";
        Items = new ItemDictionary(_itemsPath);
    }

    public string Name { get; }
    public bool RemoteTime { get; set; }
    public int SendTimeOut { get; set; } = 250;
    public ItemDictionary Items { get; }
    public event Action<uint, byte, byte[]>? FrameReceived;
    public event Action<string>? Diagnostic;
    public int LocalPort => _can?.LocalPort ?? 0;

    public void Open(string ip, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        Close();

        WriteDiagnostic($"open requested endpoint={ip}:{port}");

        var can = new Can(ip, port, OnCanDiagnostic);
        var lifetime = new CancellationTokenSource();

        can.MessageReceived += OnCanMessageReceived;

        lock (_sync)
        {
            _can = can;
            _lifetime = lifetime;
            _nextHeartbeatUtc = DateTime.UtcNow;
            _heartbeatTask = Task.Run(() => IdleLoopAsync(lifetime.Token), lifetime.Token);
            _writebackTask = Task.Run(() => WritebackLoopAsync(lifetime.Token), lifetime.Token);
        }

        WriteDiagnostic($"open completed localPort={can.LocalPort}");
    }

    public void Close()
    {
        Can? can;
        CancellationTokenSource? lifetime;
        Task? heartbeatTask;
        Task? writebackTask;

        lock (_sync)
        {
            can = _can;
            lifetime = _lifetime;
            heartbeatTask = _heartbeatTask;
            writebackTask = _writebackTask;
            _pendingCommands.Clear();

            _can = null;
            _lifetime = null;
            _heartbeatTask = null;
            _writebackTask = null;
        }

        if (can is not null)
        {
            can.MessageReceived -= OnCanMessageReceived;
            can.Diagnostic -= OnCanDiagnostic;
        }

        if (lifetime is not null)
        {
            lifetime.Cancel();
        }

        WaitForCompletion(heartbeatTask);
        WaitForCompletion(writebackTask);

        can?.Dispose();
        lifetime?.Dispose();
        WriteDiagnostic("close completed");
    }

    public void Dispose()
    {
        Close();
    }

    public void OnCanMessageReceived(uint id, byte dlc, byte[] data)
    {
        if (ShouldSample(ref _rxDispatchLogCount, 8, 100))
        {
            WriteDiagnostic($"OnCanMessageReceived id=0x{id:X3} dlc={dlc}");
        }

        if (data is null || dlc == 0)
        {
            WriteDiagnostic("OnCanMessageReceived ignored empty payload");
            return;
        }

        FrameReceived?.Invoke(id, dlc, data);

        if (id is >= 0x480 and <= 0x4FF)
        {
            HandleSubChannelPdo(id, dlc, data);
        }
        else if (id is >= 0x700 and <= 0x7FF)
        {
            HandleHeartbeat(id, dlc, data);
        }
        else
        {
            if (ShouldSample(ref _ignoredFrameLogCount, 4, 250))
            {
                WriteDiagnostic($"frame ignored id=0x{id:X3} dlc={dlc} data={FormatBytes(data, dlc)}");
            }
        }
    }

    private async Task IdleLoopAsync(CancellationToken token)
    {
        try
        {
            WriteDiagnostic("idle loop started");
            while (!token.IsCancellationRequested)
            {
                HbIdle();
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"idle loop error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task WritebackLoopAsync(CancellationToken token)
    {
        try
        {
            WriteDiagnostic("writeback loop started");
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
            WriteDiagnostic($"writeback loop error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void HandleSubChannelPdo(uint id, byte dlc, byte[] data)
    {
        if (dlc < 8 || data.Length < 8)
        {
            WriteDiagnostic($"subchannel ignored short frame id=0x{id:X3} dlc={dlc} len={data.Length}");
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
                module.Properties["LastRaw"].Value = FormatBytes(data, dlc);
                if (ShouldSample(ref _unknownTypeLogCount, 8, 100))
                {
                    WriteDiagnostic($"subchannel unknown type={type} module={module.Name}");
                }
                break;
        }
    }

    private void HandleHeartbeat(uint id, byte dlc, byte[] data)
    {
    }

    private void HbIdle()
    {
        var can = _can;
        if (can is null)
        {
            return;
        }

        if (DateTime.UtcNow < _nextHeartbeatUtc)
        {
            return;
        }

        _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(1);
        can.Transmit(new CanMessage(0x70E, new byte[] { 5, 4 }));

        if (RemoteTime)
        {
            return;
        }

        var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bytes = BitConverter.GetBytes(milliseconds);

        can.Transmit(new CanMessage(0x100, new byte[]
        {
            bytes[0],
            bytes[1],
            bytes[2],
            bytes[3],
            bytes[4],
            bytes[5],
            0x00,
            0x08
        }));
    }

    private Module GetOrCreateModule(uint moduleId)
    {
        var key = FormatModuleName(moduleId);
        if (Items.Has(key) && Items[key] is Module existingModule)
        {
            existingModule.EnsureWriteMetadata();
            return existingModule;
        }

        WriteDiagnostic($"create module {key} from moduleId=0x{moduleId:X3}");
        var module = new Module(key, _itemsPath);
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

    private void ProcessWriteSet(uint moduleId, Module module)
    {
        WriteDiagnostic($"process write set moduleId=0x{moduleId:X3} readWrite={FormatObject(TryGetWritePropertyValue(module.Read))} read={FormatObject(module.Read.Value)} setWrite={FormatObject(TryGetWritePropertyValue(module.Set))} set={FormatObject(module.Set.Value)} outWrite={FormatObject(TryGetWritePropertyValue(module.Out))} out={FormatObject(module.Out.Value)} stateWrite={FormatObject(TryGetWritePropertyValue(module.State))} state={FormatObject(module.State.Value)}");
        TryWrite(moduleId, module.Read, module.Read, 3);
        TryWriteCommand(moduleId, module);
        TryWrite(moduleId, module.Set, module.Set, 4);
        TryWrite(moduleId, module.Out, module.Out, 5);
    }

    private void OnWriteItemChanged(uint moduleId, Module module, ItemChangedEventArgs e)
    {
        if (!IsWriteTriggerProperty(e.PropertyName))
        {
            return;
        }

        WriteDiagnostic($"request changed moduleId=0x{moduleId:X3} item={e.Item.Path} parameter={e.PropertyName} value={FormatObject(TryGetWritePropertyValue(e.Item) ?? e.Item.Value)}");
        ProcessRequestWrite(moduleId, module, e.Item);
    }

    private void ProcessRequestWrite(uint moduleId, Module module, ItemModel requestItem)
    {
        if (ReferenceEquals(requestItem, module.Read))
        {
            WriteDiagnostic($"process request write moduleId=0x{moduleId:X3} channel=read write={FormatObject(TryGetWritePropertyValue(module.Read))} read={FormatObject(module.Read.Value)}");
            TryWrite(moduleId, module.Read, module.Read, 3);
            return;
        }

        if (ReferenceEquals(requestItem, module.State))
        {
            WriteDiagnostic($"process request write moduleId=0x{moduleId:X3} channel=state write={FormatObject(TryGetWritePropertyValue(module.State))} state={FormatObject(module.State.Value)}");
            TryWriteCommand(moduleId, module);
            return;
        }

        if (ReferenceEquals(requestItem, module.Set))
        {
            WriteDiagnostic($"process request write moduleId=0x{moduleId:X3} channel=set write={FormatObject(TryGetWritePropertyValue(module.Set))} set={FormatObject(module.Set.Value)}");
            TryWrite(moduleId, module.Set, module.Set, 4);
            return;
        }

        if (ReferenceEquals(requestItem, module.Out))
        {
            WriteDiagnostic($"process request write moduleId=0x{moduleId:X3} channel=out write={FormatObject(TryGetWritePropertyValue(module.Out))} out={FormatObject(module.Out.Value)}");
            TryWrite(moduleId, module.Out, module.Out, 5);
            return;
        }

        WriteDiagnostic($"process request write moduleId=0x{moduleId:X3} channel=unknown requestPath={requestItem.Path}");
    }

    private void TryWriteCommand(uint moduleId, Module module)
    {
        if (!TryGetCommandRequest(moduleId, module, out var desiredValue, out var shouldSend, out var timedOut))
        {
            WriteDiagnostic($"state write skipped moduleId=0x{moduleId:X3} reason=no-request state={FormatObject(module.State.Value)} request={FormatObject(TryGetWritePropertyValue(module.State))}");
            return;
        }

        if (timedOut)
        {
            module.Properties["SendStatus"].Value = "timeout";
            ClearRequestedValue(module.State);
            WriteDiagnostic($"state timeout moduleId=0x{moduleId:X3} desired={desiredValue:0.###}");
            return;
        }

        if (!shouldSend)
        {
            WriteDiagnostic($"state write deferred moduleId=0x{moduleId:X3} desired={desiredValue:0.###}");
            return;
        }

        WriteDiagnostic($"state write request moduleId=0x{moduleId:X3} desired={desiredValue:0.###} source={module.State.Name}");
        module.Properties["SendStatus"].Value = "sending";
        var queued = SendWritePdo(moduleId, desiredValue, 1);
        WriteDiagnostic($"state write send result moduleId=0x{moduleId:X3} desired={desiredValue:0.###} queued={queued}");
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

    private bool TryGetCommandRequest(uint moduleId, Module module, out double desiredValue, out bool shouldSend, out bool timedOut)
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
        return TimeSpan.FromMilliseconds(Math.Max(20, SendTimeOut));
    }

    private void TrackCommandState(uint moduleId, double stateValue, Module module)
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
        WriteDiagnostic($"state acknowledged moduleId=0x{moduleId:X3} value={stateValue:0.###}");
    }

    private void TryWrite(uint moduleId, ItemModel requestItem, ItemModel currentItem, int function)
    {
        WriteDiagnostic($"try write moduleId=0x{moduleId:X3} function={function} requestPath={requestItem.Path} requestValue={FormatObject(TryGetWritePropertyValue(requestItem) ?? requestItem.Value)} currentPath={currentItem.Path} currentValue={FormatObject(currentItem.Value)}");

        if (!TryGetDesiredWriteValue(requestItem, currentItem, out var desiredValue))
        {
            WriteDiagnostic($"try write skipped moduleId=0x{moduleId:X3} function={function} reason=no-desired-value requestPath={requestItem.Path}");
            return;
        }

        if (!TryConvertToDouble(currentItem.Value, out double currentValue))
        {
            WriteDiagnostic($"write request moduleId=0x{moduleId:X3} function={function} current=<unset> desired={desiredValue:0.###} source={requestItem.Path}");
            var queuedWithoutCurrent = SendWritePdo(moduleId, desiredValue, function);
            WriteDiagnostic($"write send result moduleId=0x{moduleId:X3} function={function} queued={queuedWithoutCurrent} source={requestItem.Path}");
            return;
        }

        if (Math.Abs(desiredValue - currentValue) <= 0.0001)
        {
            WriteDiagnostic($"try write skipped moduleId=0x{moduleId:X3} function={function} reason=desired-equals-current current={currentValue:0.###} desired={desiredValue:0.###} source={requestItem.Path}");
            return;
        }

        WriteDiagnostic($"write request moduleId=0x{moduleId:X3} function={function} current={currentValue:0.###} desired={desiredValue:0.###} source={requestItem.Path}");
        var queued = SendWritePdo(moduleId, desiredValue, function);
        WriteDiagnostic($"write send result moduleId=0x{moduleId:X3} function={function} queued={queued} source={requestItem.Path}");
    }

    private bool SendWritePdo(uint moduleId, double value, int function)
    {
        var can = _can;
        if (can is null)
        {
            WriteDiagnostic($"send write pdo skipped moduleId=0x{moduleId:X3} function={function} reason=no-can value={value:0.###}");
            return false;
        }

        var writeId = GetWriteIdFromModule(moduleId);
        var data = new byte[8];

        Array.Copy(BitConverter.GetBytes((float)value), 0, data, 0, 4);
        data[4] = 0;
        data[5] = 0;
        data[6] = (byte)function;
        data[7] = (byte)(moduleId & 0x0F);

        WriteDiagnostic($"send write pdo id=0x{writeId:X3} function={function} moduleId=0x{moduleId:X3} data={FormatBytes(data, 8)}");
        return can.Transmit(new CanMessage(writeId, data));
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

    private bool TryGetModule(uint moduleId, out Module module)
    {
        var key = FormatModuleName(moduleId);
        if (Items.Has(key) && Items[key] is Module existingModule)
        {
            module = existingModule;
            return true;
        }

        module = null!;
        return false;
    }

    private static bool TryGetModuleId(Module module, out uint moduleId)
    {
        moduleId = 0;
        if (!module.Properties.Has("module_id"))
        {
            return false;
        }

        var value = module.Properties["module_id"].Value;
        return TryConvertToUInt32(value, out moduleId);
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

    private static bool HasRequestedValue(ItemModel item)
    {
        return TryGetRequestItemValue(item, out _)
            || item.Properties.Has("set")
            || item.Properties.Has("write")
            || item.Properties.Has("Set")
            || item.Properties.Has("Write");
    }

    private static void InitializeRequestValueIfMissing(ItemModel item)
    {
        if (!item.Has("Request"))
        {
            return;
        }

        if (item["Request"].Value is null)
        {
            item["Request"].Value = item.Value;
        }
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

    private static bool TryConvertToUInt32(object? value, out uint converted)
    {
        converted = 0;
        if (value is null)
        {
            return false;
        }

        try
        {
            converted = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
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
            parts[index] = data[index].ToString("X2");
        }

        return string.Join(" ", parts);
    }

    private static string FormatModuleName(uint moduleId)
        => $"m{moduleId:X3}";

    private static string FormatObject(object? value)
        => value is null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";

    private static bool ShouldSample(ref long counter, long initialBurst, long every)
    {
        var current = Interlocked.Increment(ref counter);
        return current <= initialBurst || current % every == 0;
    }

    private void OnCanDiagnostic(string message)
    {
        WriteDiagnostic(message);
    }

    private void WriteDiagnostic(string message)
    {
        var formatted = $"[UdlClient:{Name}] {message}";
        try
        {
            Diagnostic?.Invoke(formatted);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"UdlClient diagnostic callback failed: {exception}");
        }
    }

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
