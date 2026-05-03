using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Host;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Widgets;

public sealed class SimulatedHostUdlClient : IHostUdlClient
{
    private sealed class ModuleRuntimeState
    {
        public required UdlDemoModuleDefinition Definition { get; init; }

        public required Item Module { get; init; }

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
        _itemsPath = $"Runtime.UdlClient.{Name}";
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
            module.Params["ModuleId"].Value = moduleId;
            module.Params["Text"].Value = definition.Name.Trim();
            module.Params["Kind"].Value = "UdlModule";
            module.Params["Demo"].Value = true;
            module.Params["DemoKind"].Value = definition.Kind.ToString();
            module.Params["Generator"].Value = definition.Generator.ToString();
            module.Params["Unit"].Value = definition.Unit;
            module.Params["Format"].Value = definition.Format;

            var read = GetChannel(module, "Read");
            read.Params["Text"].Value = $"{definition.Name} Read";
            read.Params["Unit"].Value = definition.Unit;
            read.Params["Format"].Value = definition.Format;
            GetRequest(read).Params["Text"].Value = $"{definition.Name} Read Request";

            var set = GetChannel(module, "Set");
            set.Params["Text"].Value = $"{definition.Name} Set";
            set.Params["Unit"].Value = definition.Unit;
            set.Params["Format"].Value = definition.Format;
            var setRequest = GetRequest(set);
            setRequest.Params["Text"].Value = $"{definition.Name} Set Request";
            setRequest.Value = definition.InitialValue;

            var output = GetChannel(module, "Out");
            output.Params["Text"].Value = $"{definition.Name} Out";
            output.Params["Unit"].Value = definition.Unit;
            output.Params["Format"].Value = definition.Format;
            GetRequest(output).Params["Text"].Value = $"{definition.Name} Out Request";

            GetChannel(module, "State").Params["Text"].Value = $"{definition.Name} State";
            GetChannel(module, "Alert").Params["Text"].Value = $"{definition.Name} Alert";
            var command = GetChannel(module, "Command");
            command.Params["Text"].Value = $"{definition.Name} Command";
            var commandRequest = GetRequest(command);
            commandRequest.Params["Text"].Value = $"{definition.Name} Command Request";
            commandRequest.Value = 1;

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
        var requestValue = TryReadDouble(GetRequest(GetChannel(state.Module, "Set")).Value, definition.InitialValue);
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
        read.Value = value;
        InitializeRequestValueIfMissing(read);

        var set = GetChannel(state.Module, "Set");
        set.Value = value;
        InitializeRequestValueIfMissing(set);

        var output = GetChannel(state.Module, "Out");
        output.Value = value;
        InitializeRequestValueIfMissing(output);

        GetChannel(state.Module, "State").Value = stateCode;
        GetChannel(state.Module, "Alert").Value = alertText;
        var command = GetChannel(state.Module, "Command");
        command.Value = stateCode;
        InitializeRequestValueIfMissing(command);
    }

    private void RaiseDiagnostic(string message)
    {
        Diagnostic?.Invoke(message);
    }

    private static UdlDemoFaultDefinition? GetNoiseFault(UdlDemoModuleDefinition definition)
        => definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Noise);

    private static UdlDemoFaultDefinition? GetFreezeFault(UdlDemoModuleDefinition definition)
        => definition.Faults.FirstOrDefault(static fault => fault.Kind == UdlDemoFaultKind.Freeze);

    private static Item CreateModule(UdlDemoModuleDefinition definition, string itemsPath)
    {
        var module = new Item(definition.Name.Trim(), path: itemsPath);
        module.Params["Kind"].Value = "UdlModule";
        module.Params["Text"].Value = definition.Name.Trim();
        module.Params["Unit"].Value = string.Empty;

        AddRequestChannel(module, "Read");
        AddRequestChannel(module, "Set");
        AddRequestChannel(module, "Out");
        AddItem(module, "State");
        AddItem(module, "Alert");
        AddRequestChannel(module, "Command");
        return module;
    }

    private static Item GetChannel(Item module, string name) => module[name];

    private static Item GetRequest(Item channel) => channel["Request"];

    private static void AddRequestChannel(Item module, string name)
    {
        AddItem(module, name);
        var channel = module[name];
        AddItem(channel, "Request");
        channel["Request"].Params["Text"].Value = $"{name} Request";
        channel["Request"].Value = channel.Value;
    }

    private static void AddItem(Item parent, string name)
    {
        if (!parent.Has(name))
        {
            parent.AddItem(name);
        }
    }

    private static void InitializeRequestValueIfMissing(Item item)
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
