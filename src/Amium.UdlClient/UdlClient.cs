using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.Items;
using ItemChangedEventArgs = Amium.Items.ItemChangedEventArgs;
using ItemDictionary = Amium.Items.ItemDictionary;

namespace Amium.UdlClient;

public sealed class UdlClient : IDisposable
{
    private readonly record struct PendingWriteKey(uint ModuleId, int Function);

    private sealed class PendingWrite
    {
        public PendingWrite(float desiredValue, DateTime firstAttemptUtc)
        {
            DesiredValue = desiredValue;
            FirstAttemptUtc = firstAttemptUtc;
            LastSendUtc = DateTime.MinValue;
        }

        public float DesiredValue { get; set; }
        public DateTime FirstAttemptUtc { get; set; }
        public DateTime LastSendUtc { get; set; }
    }

    private readonly string _itemsPath;
    private readonly object _sync = new();
    private readonly Dictionary<PendingWriteKey, PendingWrite> _pendingWrites = new();
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

        var can = new Can(ip, port, WriteDiagnostic);
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
            _pendingWrites.Clear();

            _can = null;
            _lifetime = null;
            _heartbeatTask = null;
            _writebackTask = null;
        }

        if (can is not null)
        {
            can.MessageReceived -= OnCanMessageReceived;
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
                SendHeartbeatIfDue();
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
                foreach (var key in GetPendingWriteKeys())
                {
                    if (!TryGetModule(key.ModuleId, out var module))
                    {
                        continue;
                    }

                    TrySendPendingWrite(key, module);
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
        var function = data[6];
        var rawValue = BitConverter.ToSingle(data, 0);
        switch (function)
        {
            case 1:
            {
                var stateValue = Convert.ToInt32(Math.Round(rawValue, MidpointRounding.AwayFromZero));
                module.State.Properties["read"].Value = stateValue;
                AcknowledgePendingWrite(moduleId, function: function, stateValue, module, module.State);
                return;
            }

            case 2:
                module.Alert.Properties["read"].Value = rawValue;
                return;

            case 3:
            {
                module.Read.Properties["read"].Value = rawValue;
                var metadata = (ushort)(data[4] | (data[5] << 8));
                module.Read.Properties["MetaData"].Value = metadata;
                module.Properties["MetaData"].Value = metadata;
                AcknowledgePendingWrite(moduleId, function: function, rawValue, module, module.Read);
                return;
            }

            case 4:
                module.Set.Properties["read"].Value = rawValue;
                AcknowledgePendingWrite(moduleId, function: function, rawValue, module, module.Set);
                return;

            case 5:
                module.Out.Properties["read"].Value = rawValue;
                AcknowledgePendingWrite(moduleId, function: function, rawValue, module, module.Out);
                return;

            default:
                module.Properties["LastType"].Value = function;
                module.Properties["LastRaw"].Value = FormatBytes(data, dlc);
                if (ShouldSample(ref _unknownTypeLogCount, 8, 100))
                {
                    WriteDiagnostic($"subchannel unknown type={function} module={module.Name}");
                }

                return;
        }
    }
    private void HandleHeartbeat(uint id, byte dlc, byte[] data)
    {
    }

    private void SendHeartbeatIfDue()
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
            return existingModule;
        }

        WriteDiagnostic($"create module {key} from moduleId=0x{moduleId:X3}");
        var module = new Module(key, _itemsPath);
        module.Properties["module_id"].Value = moduleId;
        module.Properties["text"].Value = key;
        module.Properties["kind"].Value = "UdlModule";
        module.Properties["send_status"].Value = "idle";

        module.Read.Properties["text"].Value = $"{key} Read";
        module.Set.Properties["text"].Value = $"{key} Set";
        module.Out.Properties["text"].Value = $"{key} Out";
        module.State.Properties["text"].Value = $"{key} State";
        module.Alert.Properties["text"].Value = $"{key} Alert";

        module.Read.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.Set.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.Out.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
        module.State.Changed += (_, e) => OnWriteItemChanged(moduleId, module, e);
      

        Items[key] = module;
        return module;
    }

    private void OnWriteItemChanged(uint moduleId, Module module, ItemChangedEventArgs e)
    {
        if (!IsWriteTriggerProperty(e.PropertyName))
        {
            return;
        }

        var requestItem = e.Item;
        var function = 0;
        var channelName = string.Empty;

        if (ReferenceEquals(requestItem, module.State))
        {
            function = 1;
            channelName = "state";
        }
        else if (ReferenceEquals(requestItem, module.Read))
        {
            function = 3;
            channelName = "read";
        }
        else if (ReferenceEquals(requestItem, module.Set))
        {
            function = 4;
            channelName = "set";
        }
        else if (ReferenceEquals(requestItem, module.Out))
        {
            function = 5;
            channelName = "out";
        }

        WriteDiagnostic($"request changed moduleId=0x{moduleId:X3} item={requestItem.Path} parameter={e.PropertyName} value={FormatObject(TryGetWritePropertyValue(requestItem) ?? requestItem.Value)}");

        if (function == 0)
        {
            WriteDiagnostic($"write request ignored moduleId=0x{moduleId:X3} channel=unknown requestPath={requestItem.Path}");
            return;
        }

        WriteDiagnostic($"write request moduleId=0x{moduleId:X3} channel={channelName} write={FormatObject(TryGetWritePropertyValue(requestItem))} read={FormatObject(TryGetReadPropertyValue(requestItem))}");
        QueuePendingWrite(moduleId, function, requestItem, module);
    }
    private void QueuePendingWrite(uint moduleId, int function, Item item, Module module)
    {
        if (!TryGetWriteValue(item, out var desiredValue))
        {
            WriteDiagnostic($"write skipped moduleId=0x{moduleId:X3} function={function} reason=no-desired-value requestPath={item.Path}");
            return;
        }

        var key = new PendingWriteKey(moduleId, function);

        if (TryGetReadValue(item, out var currentValue) && MathF.Abs(desiredValue - currentValue) <= 0.0001f)
        {
            lock (_sync)
            {
                _pendingWrites.Remove(key);
            }

            WriteDiagnostic($"write skipped moduleId=0x{moduleId:X3} function={function} reason=desired-equals-current current={currentValue:0.###} desired={desiredValue:0.###} source={item.Path}");
            return;
        }

        lock (_sync)
        {
            if (!_pendingWrites.TryGetValue(key, out var pending)
                || MathF.Abs(pending.DesiredValue - desiredValue) > 0.0001f)
            {
                _pendingWrites[key] = new PendingWrite(desiredValue, DateTime.UtcNow);
            }
        }

        module.Properties["send_status"].Value = "pending";
        WriteDiagnostic($"write queued moduleId=0x{moduleId:X3} function={function} desired={desiredValue:0.###} source={item.Path}");
    }

    private TimeSpan GetSendTimeout()
    {
        return TimeSpan.FromMilliseconds(Math.Max(20, SendTimeOut));
    }

    private void TrySendPendingWrite(PendingWriteKey key, Module module)
    {
        float desiredValue;
        bool timedOut;
        bool shouldSend;

        lock (_sync)
        {
            if (!_pendingWrites.TryGetValue(key, out var pending))
            {
                return;
            }

            var now = DateTime.UtcNow;
            desiredValue = pending.DesiredValue;
            timedOut = now - pending.FirstAttemptUtc >= GetSendTimeout();
            shouldSend = pending.LastSendUtc == DateTime.MinValue || now - pending.LastSendUtc >= TimeSpan.FromMilliseconds(20);

            if (timedOut)
            {
                _pendingWrites.Remove(key);
            }
        }

        if (timedOut)
        {
            if (TryGetChannelByFunction(module, key.Function, out var item, out _))
            {
                ClearRequestedValue(item);
            }

            module.Properties["send_status"].Value = "timeout";
            WriteDiagnostic($"write timeout moduleId=0x{key.ModuleId:X3} function={key.Function} desired={desiredValue:0.###}");
            return;
        }

        if (!shouldSend)
        {
            WriteDiagnostic($"write deferred moduleId=0x{key.ModuleId:X3} function={key.Function} desired={desiredValue:0.###}");
            return;
        }

        WriteDiagnostic($"write request moduleId=0x{key.ModuleId:X3} function={key.Function} desired={desiredValue:0.###}");
        module.Properties["send_status"].Value = "sending";
        var queued = SendWritePdo(key.ModuleId, desiredValue, key.Function);
        WriteDiagnostic($"write send result moduleId=0x{key.ModuleId:X3} function={key.Function} desired={desiredValue:0.###} queued={queued}");

        if (!queued)
        {
            return;
        }

        lock (_sync)
        {
            if (_pendingWrites.TryGetValue(key, out var pending))
            {
                pending.LastSendUtc = DateTime.UtcNow;
            }
        }
    }
    private void AcknowledgePendingWrite(uint moduleId, int function, float receivedValue, Module module, Item item)
    {
        var key = new PendingWriteKey(moduleId, function);
        var acknowledged = false;

        lock (_sync)
        {
            if (!_pendingWrites.TryGetValue(key, out var pending))
            {
                return;
            }

            if (pending.LastSendUtc != DateTime.MinValue && DateTime.UtcNow <= pending.LastSendUtc)
            {
                return;
            }

            if (MathF.Abs(pending.DesiredValue - receivedValue) > 0.0001f)
            {
                return;
            }

            _pendingWrites.Remove(key);
            acknowledged = true;
        }

        if (!acknowledged)
        {
            return;
        }

        ClearRequestedValue(item);
        module.Properties["send_status"].Value = "ok";
        WriteDiagnostic($"write acknowledged moduleId=0x{moduleId:X3} function={function} value={receivedValue:0.###}");
    }
    private bool SendWritePdo(uint moduleId, float value, int function)
    {
        var can = _can;
        if (can is null)
        {
            WriteDiagnostic($"send write pdo skipped moduleId=0x{moduleId:X3} function={function} reason=no-can value={value:0.###}");
            return false;
        }

        var writeId = GetWriteIdFromModule(moduleId);
        var data = new byte[8];

        Array.Copy(BitConverter.GetBytes(value), 0, data, 0, 4);
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

    private PendingWriteKey[] GetPendingWriteKeys()
    {
        lock (_sync)
        {
            return _pendingWrites.Keys.ToArray();
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

    private static bool TryGetChannelByFunction(Module module, int function, out Item item, out string channelName)
    {
        switch (function)
        {
            case 1:
                item = module.State;
                channelName = "state";
                return true;
            case 3:
                item = module.Read;
                channelName = "read";
                return true;
            case 4:
                item = module.Set;
                channelName = "set";
                return true;
            case 5:
                item = module.Out;
                channelName = "out";
                return true;
            default:
                item = null!;
                channelName = string.Empty;
                return false;
        }
    }

    private static bool TryGetWriteValue(Item item, out float value)
    {
        value = 0;
        return item.Properties.Has("write")
            && TryConvertToFloat(item.Properties["write"].Value, out value)
            && !float.IsNaN(value);
    }

    private static bool TryGetReadValue(Item item, out float value)
    {
        value = 0;
        return item.Properties.Has("read")
            && TryConvertToFloat(item.Properties["read"].Value, out value)
            && !float.IsNaN(value);
    }

    private static void ClearRequestedValue(Item item)
    {
        if (item.Properties.Has("write"))
        {
            item.Properties["write"].Value = item.Properties.Has("read")
                ? item.Properties["read"].Value
                : null!;
        }
        item.Properties.Remove("Set");
        item.Properties.Remove("Write");
        item.Properties.Remove("set");
    }

    private static bool IsWriteTriggerProperty(string propertyName)
        => string.Equals(propertyName, "write", StringComparison.OrdinalIgnoreCase);

    private static object? TryGetWritePropertyValue(Item item)
        => item.Properties.Has("write")
            ? item.Properties["write"].Value
            : null;

    private static object? TryGetReadPropertyValue(Item item)
        => item.Properties.Has("read")
            ? item.Properties["read"].Value
            : null;
    private static bool TryConvertToFloat(object? value, out float converted)
    {
        converted = 0;
        if (value is null)
        {
            return false;
        }

        switch (value)
        {
            case float floatValue:
                converted = floatValue;
                return true;
            case double doubleValue:
                converted = (float)doubleValue;
                return !float.IsNaN(converted) && !float.IsInfinity(converted);
            case string text when float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed):
                converted = parsed;
                return true;
            default:
                try
                {
                    converted = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    return !float.IsNaN(converted) && !float.IsInfinity(converted);
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
        => $"m{moduleId:x3}";

    private static string FormatObject(object? value)
        => value is null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";

    private static bool ShouldSample(ref long counter, long initialBurst, long every)
    {
        var current = Interlocked.Increment(ref counter);
        return current <= initialBurst || current % every == 0;
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
