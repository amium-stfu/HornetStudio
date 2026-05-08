using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Widgets;

public sealed class SimulatedHostUdlClient : IHostUdlClient
{
    private sealed class ModuleRuntimeState
    {
        public required UdlDemoModuleDefinition Definition { get; init; }

        public required ItemModel Module { get; init; }

        public required uint ModuleId { get; init; }

        public double CurrentValue { get; set; }

        public double SettledValue { get; set; }

        public DateTimeOffset LastUpdateUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset FreezeUntilUtc { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset NextFreezeAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public double FrozenValue { get; set; }

        public double HeldNoiseValue { get; set; }

        public DateTimeOffset NextNoiseUpdateAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset PeakNoiseUntilUtc { get; set; } = DateTimeOffset.MinValue;

        public string AlertText { get; set; } = string.Empty;

        public int StateCode { get; set; } = 1;
    }

    private readonly string _itemsPath;
    private readonly List<UdlDemoModuleDefinition> _definitions;
    private readonly List<ModuleRuntimeState> _moduleStates = [];
    private readonly Random _random = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _lifetime;
    private Task? _simulationTask;
    private DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public SimulatedHostUdlClient(string name, string host, int port, IEnumerable<UdlDemoModuleDefinition>? definitions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A client name is required.", nameof(name));
        }

        Name = name.Trim();
        Host = string.IsNullOrWhiteSpace(host) ? "demo" : host.Trim();
        Port = port <= 0 ? 9001 : port;
        _itemsPath = UdlPathHelper.GetCanonicalRuntimeBasePath(Name);
        Items = new ItemDictionary(_itemsPath);
        _definitions = definitions?
            .Where(static definition => definition is not null)
            .Select(static definition => definition.Clone())
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToList()
            ?? [];
    }

    public string Name { get; }

    public string Host { get; }

    public int Port { get; }

    public bool IsConnected => _lifetime is not null && !_lifetime.IsCancellationRequested;

    public int LocalPort => 0;

    public ItemDictionary Items { get; }

    public event Action<uint, byte, byte[]>? FrameReceived;

    public event Action<string>? Diagnostic;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_lifetime is not null)
            {
                return Task.CompletedTask;
            }

            _startedUtc = DateTimeOffset.UtcNow;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            BuildModules();
            _simulationTask = Task.Run(() => SimulationLoopAsync(_lifetime.Token), _lifetime.Token);
        }

        RaiseDiagnostic($"[SimulatedHostUdlClient:{Name}] connect completed modules={_moduleStates.Count}");
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime;
        Task? simulationTask;

        lock (_sync)
        {
            lifetime = _lifetime;
            simulationTask = _simulationTask;
            _lifetime = null;
            _simulationTask = null;
        }

        if (lifetime is null)
        {
            return;
        }

        lifetime.Cancel();

        if (simulationTask is not null)
        {
            try
            {
                await simulationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lifetime.Dispose();
        RaiseDiagnostic($"[SimulatedHostUdlClient:{Name}] disconnect completed");
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    private void BuildModules()
    {
        _moduleStates.Clear();

        for (var index = 0; index < _definitions.Count; index++)
        {
            var definition = _definitions[index];
            var moduleId = (uint)(index + 1);
            var module = CreateModule(definition, _itemsPath);
            module.Properties["module_id"].Value = moduleId;
            module.Properties["text"].Value = definition.Name.Trim();
            module.Properties["kind"].Value = "UdlModule";
            module.Properties["demo"].Value = true;
            module.Properties["demo_kind"].Value = definition.Kind.ToString();
            module.Properties["generator"].Value = definition.Generator.ToString();
            module.Properties["unit"].Value = definition.Unit;
            module.Properties["format"].Value = definition.Format;

            var read = GetChannel(module, "Read");
            read.Properties["text"].Value = $"{definition.Name} Read";
            read.Properties["unit"].Value = definition.Unit;
            read.Properties["format"].Value = definition.Format;
            read.Properties["write"].Value = definition.InitialValue;

            var set = GetChannel(module, "Set");
            set.Properties["text"].Value = $"{definition.Name} Set";
            set.Properties["unit"].Value = definition.Unit;
            set.Properties["format"].Value = definition.Format;
            set.Properties["write"].Value = definition.InitialValue;

            var output = GetChannel(module, "Out");
            output.Properties["text"].Value = $"{definition.Name} Out";
            output.Properties["unit"].Value = definition.Unit;
            output.Properties["format"].Value = definition.Format;
            output.Properties["write"].Value = definition.InitialValue;

            var stateChannel = GetChannel(module, "State");
            stateChannel.Properties["text"].Value = $"{definition.Name} State";
            stateChannel.Properties["write"].Value = 1;
            GetChannel(module, "Alert").Properties["text"].Value = $"{definition.Name} Alert";

            var state = new ModuleRuntimeState
            {
                Definition = definition,
                Module = module,
                ModuleId = moduleId,
                CurrentValue = definition.InitialValue,
                SettledValue = definition.InitialValue,
                LastUpdateUtc = DateTimeOffset.UtcNow,
                NextFreezeAtUtc = DateTimeOffset.UtcNow.AddSeconds(GetFreezeFault(definition)?.IntervalSeconds ?? 5),
                NextNoiseUpdateAtUtc = DateTimeOffset.UtcNow
            };

            ApplyModuleSnapshot(state, definition.InitialValue, string.Empty, 1);
            Items[definition.Name.Trim()] = module;
            _moduleStates.Add(state);
            RaiseDiagnostic($"[SimulatedHostUdlClient:{Name}] create module {definition.Name.Trim()} mode={definition.Kind}");
        }
    }

    private async Task SimulationLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var elapsedSeconds = (now - _startedUtc).TotalSeconds;

                foreach (var state in _moduleStates)
                {
                    UpdateModuleState(state, now, elapsedSeconds);
                    FrameReceived?.Invoke(state.ModuleId, 0, Array.Empty<byte>());
                }

                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RaiseDiagnostic($"[SimulatedHostUdlClient:{Name}] simulation error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UpdateModuleState(ModuleRuntimeState state, DateTimeOffset now, double elapsedSeconds)
    {
        var targetValue = state.Definition.Kind switch
        {
            UdlDemoModuleKind.SetDriven => ComputeSetDrivenValue(state, now),
            _ => ComputeDynamicValue(state.Definition, elapsedSeconds)
        };

        var alertParts = new List<string>();
        var stateCode = 1;
        var value = targetValue;

        var freezeFault = GetFreezeFault(state.Definition);
        if (freezeFault?.Enabled == true)
        {
            if (state.FreezeUntilUtc > now)
            {
                value = state.FrozenValue;
                stateCode = 2;
                alertParts.Add("Freeze");
            }
            else if (now >= state.NextFreezeAtUtc)
            {
                state.FrozenValue = state.CurrentValue;
                state.FreezeUntilUtc = now.AddMilliseconds(freezeFault.DurationMs <= 0 ? 1000 : freezeFault.DurationMs);
                state.NextFreezeAtUtc = now.AddSeconds(freezeFault.IntervalSeconds <= 0 ? 5 : freezeFault.IntervalSeconds);
                value = state.FrozenValue;
                stateCode = 2;
                alertParts.Add("Freeze");
            }
        }

        var noiseFault = GetNoiseFault(state.Definition);
        if (TryComputeNoise(state, noiseFault, now, elapsedSeconds, out var noise, out var noiseDescription))
        {
            value += noise;
            alertParts.Add($"Noise {noiseDescription} {noise:0.###}");
        }

        state.CurrentValue = value;
        state.StateCode = stateCode;
        state.AlertText = string.Join(" | ", alertParts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        ApplyModuleSnapshot(state, value, state.AlertText, stateCode);
        state.LastUpdateUtc = now;
    }

    private static double ComputeDynamicValue(UdlDemoModuleDefinition definition, double elapsedSeconds)
    {
        var periodSeconds = definition.PeriodSeconds <= 0 ? 5 : definition.PeriodSeconds;
        return definition.Generator switch
        {
            UdlDemoGeneratorKind.Ramp => definition.BaseValue + definition.Amplitude * ((elapsedSeconds % periodSeconds) / periodSeconds),
            _ => definition.BaseValue + definition.Amplitude * Math.Sin((elapsedSeconds / periodSeconds) * Math.PI * 2d)
        };
    }

    private static double ComputeSetDrivenValue(ModuleRuntimeState state, DateTimeOffset now)
    {
        var definition = state.Definition;
        var requestValue = TryReadDouble(GetChannel(state.Module, "Set").Properties["write"].Value, definition.InitialValue);
        var targetValue = requestValue * definition.SetScale + definition.SetOffset;
        var tauSeconds = Math.Max(0, definition.SetTauSeconds);

        if (tauSeconds <= double.Epsilon)
        {
            state.SettledValue = targetValue;
            return targetValue;
        }

        var deltaSeconds = Math.Max(0, (now - state.LastUpdateUtc).TotalSeconds);
        if (deltaSeconds <= double.Epsilon)
        {
            return state.SettledValue;
        }

        var alpha = 1d - Math.Exp(-deltaSeconds / tauSeconds);
        state.SettledValue += (targetValue - state.SettledValue) * alpha;
        return state.SettledValue;
    }

    private bool TryComputeNoise(ModuleRuntimeState state, UdlDemoFaultDefinition? noiseFault, DateTimeOffset now, double elapsedSeconds, out double noise, out string description)
    {
        noise = 0;
        description = string.Empty;

        if (noiseFault?.Enabled != true || (Math.Abs(noiseFault.Amount) <= double.Epsilon && Math.Abs(noiseFault.PeakAmount) <= double.Epsilon))
        {
            state.HeldNoiseValue = 0;
            state.NextNoiseUpdateAtUtc = now;
            state.PeakNoiseUntilUtc = DateTimeOffset.MinValue;
            return false;
        }

        var amount = Math.Abs(noiseFault.Amount);
        var peakAmount = Math.Abs(noiseFault.PeakAmount) <= double.Epsilon ? amount : Math.Abs(noiseFault.PeakAmount);
        var periodSeconds = noiseFault.PeriodSeconds <= 0 ? 2 : noiseFault.PeriodSeconds;
        var updateIntervalMs = noiseFault.UpdateIntervalMs <= 0 ? 250 : noiseFault.UpdateIntervalMs;
        var peakDurationMs = noiseFault.DurationMs <= 0 ? 100 : noiseFault.DurationMs;

        var descriptionParts = new List<string>();

        if (noiseFault.UseSine)
        {
            noise += amount * Math.Sin((elapsedSeconds / periodSeconds) * Math.PI * 2d);
            descriptionParts.Add("Sine");
        }

        if (noiseFault.UseJitter)
        {
            noise += (_random.NextDouble() * 2d - 1d) * amount;
            descriptionParts.Add("Jitter");
        }

        if (noiseFault.UsePeak)
        {
            if (now >= state.NextNoiseUpdateAtUtc)
            {
                state.HeldNoiseValue = (_random.NextDouble() * 2d - 1d) * peakAmount;
                state.PeakNoiseUntilUtc = now.AddMilliseconds(peakDurationMs);
                state.NextNoiseUpdateAtUtc = now.AddMilliseconds(Math.Max(updateIntervalMs, peakDurationMs));
            }

            if (now < state.PeakNoiseUntilUtc)
            {
                noise += state.HeldNoiseValue;
            }

            descriptionParts.Add($"Peak/{updateIntervalMs}ms/{peakDurationMs}ms");
        }

        description = string.Join("+", descriptionParts);
        return Math.Abs(noise) > double.Epsilon;
    }

    private static void ApplyModuleSnapshot(ModuleRuntimeState state, double value, string alertText, int stateCode)
    {
        state.Module.Value = value;

        var read = GetChannel(state.Module, "Read");
        SetReadValue(read, value);

        var set = GetChannel(state.Module, "Set");
        SetReadValue(set, value);

        var output = GetChannel(state.Module, "Out");
        SetReadValue(output, value);

        SetReadValue(GetChannel(state.Module, "State"), stateCode);
        GetChannel(state.Module, "Alert").Value = alertText;
    }

    private void RaiseDiagnostic(string message)
    {
        Diagnostic?.Invoke(message);
    }

    private static UdlDemoFaultDefinition? GetNoiseFault(UdlDemoModuleDefinition definition)
        => definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Noise);

    private static UdlDemoFaultDefinition? GetFreezeFault(UdlDemoModuleDefinition definition)
        => definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Freeze);

    private static ItemModel CreateModule(UdlDemoModuleDefinition definition, string itemsPath)
    {
        var module = new ItemModel(definition.Name.Trim(), path: itemsPath);
        module.Properties["kind"].Value = "UdlModule";
        module.Properties["text"].Value = definition.Name.Trim();
        module.Properties["unit"].Value = string.Empty;

        AddChannel(module, "Read", hasWriteChannel: true);
        AddChannel(module, "Set", hasWriteChannel: true);
        AddChannel(module, "Out", hasWriteChannel: true);
        AddChannel(module, "State", hasWriteChannel: true);
        AddChannel(module, "Alert", hasReadChannel: false);
        return module;
    }

    private static ItemModel GetChannel(ItemModel module, string name) => module[name];

    private static void AddChannel(ItemModel module, string name, bool hasWriteChannel = false, bool hasReadChannel = true)
    {
        module[name] = new ItemModel(
            name,
            path: module.Path,
            hasWriteChannel: hasWriteChannel,
            hasReadChannel: hasReadChannel);
    }

    private static void AddItem(ItemModel parent, string name)
    {
        if (!parent.Has(name))
        {
            parent.AddItem(name);
        }
    }

    private static void SetReadValue(ItemModel item, object? value)
    {
        item.Properties["read"].Value = value!;
    }

    private static double TryReadDouble(object? value, double fallback)
    {
        return value switch
        {
            null => fallback,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            decimal decimalValue => (double)decimalValue,
            string text when double.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }
}
