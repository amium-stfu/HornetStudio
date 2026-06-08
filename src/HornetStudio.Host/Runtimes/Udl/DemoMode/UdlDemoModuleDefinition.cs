using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HornetStudio.Host.Runtimes.Udl.DemoMode;

public enum UdlDemoModuleKind
{
    Dynamic,
    SetDriven
}

public enum UdlDemoGeneratorKind
{
    Sine,
    Ramp
}

public enum UdlDemoFaultKind
{
    Freeze,
    Noise
}

public enum UdlDemoNoiseMode
{
    Jitter,
    Sine,
    SineWithJitter,
    PeakJitter
}

public sealed class UdlDemoFaultDefinition
{
    public UdlDemoFaultKind Kind { get; set; } = UdlDemoFaultKind.Noise;

    public bool Enabled { get; set; }

    public double Amount { get; set; }

    public double PeakAmount { get; set; }

    public bool UseJitter { get; set; } = true;

    public bool UseSine { get; set; }

    public bool UsePeak { get; set; }

    public UdlDemoNoiseMode NoiseMode { get; set; } = UdlDemoNoiseMode.Jitter;

    public double PeriodSeconds { get; set; } = 2;

    public int UpdateIntervalMs { get; set; } = 250;

    public double IntervalSeconds { get; set; } = 5;

    public int DurationMs { get; set; } = 1000;

    public UdlDemoFaultDefinition Clone()
    {
        return new UdlDemoFaultDefinition
        {
            Kind = Kind,
            Enabled = Enabled,
            Amount = Amount,
            PeakAmount = PeakAmount,
            UseJitter = UseJitter,
            UseSine = UseSine,
            UsePeak = UsePeak,
            NoiseMode = NoiseMode,
            PeriodSeconds = PeriodSeconds,
            UpdateIntervalMs = UpdateIntervalMs,
            IntervalSeconds = IntervalSeconds,
            DurationMs = DurationMs
        };
    }
}

public sealed class UdlDemoModuleDefinition
{
    public string Name { get; set; } = string.Empty;

    public UdlDemoModuleKind Kind { get; set; } = UdlDemoModuleKind.Dynamic;

    public UdlDemoGeneratorKind Generator { get; set; } = UdlDemoGeneratorKind.Sine;

    public string Unit { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public double BaseValue { get; set; }

    public double Amplitude { get; set; } = 1;

    public double PeriodSeconds { get; set; } = 5;

    public double InitialValue { get; set; }

    public double SetScale { get; set; } = 1;

    public double SetOffset { get; set; }

    public double SetTauSeconds { get; set; }

    public List<UdlDemoFaultDefinition> Faults { get; set; } = [];

    public UdlDemoModuleDefinition Clone()
    {
        return new UdlDemoModuleDefinition
        {
            Name = Name,
            Kind = Kind,
            Generator = Generator,
            Unit = Unit,
            Format = Format,
            BaseValue = BaseValue,
            Amplitude = Amplitude,
            PeriodSeconds = PeriodSeconds,
            InitialValue = InitialValue,
            SetScale = SetScale,
            SetOffset = SetOffset,
            SetTauSeconds = SetTauSeconds,
            Faults = Faults
                .Where(static fault => fault is not null)
                .Select(static fault => fault.Clone())
                .ToList()
        };
    }
}

public sealed class UdlDemoFaultDefinitionDocument
{
    public UdlDemoFaultKind Kind { get; init; } = UdlDemoFaultKind.Noise;

    public bool Enabled { get; init; }

    public double Amount { get; init; }

    public double PeakAmount { get; init; }

    public bool UseJitter { get; init; } = true;

    public bool UseSine { get; init; }

    public bool UsePeak { get; init; }

    public UdlDemoNoiseMode NoiseMode { get; init; } = UdlDemoNoiseMode.Jitter;

    public double PeriodSeconds { get; init; } = 2;

    public int UpdateIntervalMs { get; init; } = 250;

    public double IntervalSeconds { get; init; } = 5;

    public int DurationMs { get; init; } = 1000;
}

public sealed class UdlDemoModuleDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public UdlDemoModuleKind Kind { get; init; } = UdlDemoModuleKind.Dynamic;

    public UdlDemoGeneratorKind Generator { get; init; } = UdlDemoGeneratorKind.Sine;

    public string Unit { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public double BaseValue { get; init; }

    public double Amplitude { get; init; } = 1;

    public double PeriodSeconds { get; init; } = 5;

    public double InitialValue { get; init; }

    public double SetScale { get; init; } = 1;

    public double SetOffset { get; init; }

    public double SetTauSeconds { get; init; }

    public List<UdlDemoFaultDefinitionDocument> Faults { get; init; } = [];
}

public static class UdlDemoModuleDefinitionCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<UdlDemoModuleDefinition> ParseDefinitions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<UdlDemoModuleDefinition>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<UdlDemoModuleDefinition>>(raw, JsonOptions);
            return parsed?
                .Where(static definition => definition is not null)
                .Select(static definition => NormalizeDefinition(definition!))
                .ToArray()
                ?? Array.Empty<UdlDemoModuleDefinition>();
        }
        catch
        {
            return Array.Empty<UdlDemoModuleDefinition>();
        }
    }

    public static string SerializeDefinitions(IEnumerable<UdlDemoModuleDefinition>? definitions)
    {
        var normalized = definitions?
            .Where(static definition => definition is not null)
            .Select(static definition => definition!)
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToArray()
            ?? Array.Empty<UdlDemoModuleDefinition>();

        return normalized.Length == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static JsonArray ToJsonArray(string? rawDefinitions)
    {
        var array = new JsonArray();
        foreach (var definition in ParseDefinitions(rawDefinitions))
        {
            array.Add(JsonSerializer.SerializeToNode(ToDocument(definition), JsonOptions));
        }

        return array;
    }

    public static string FromJsonNode(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            var definitions = array
                .Select(child => child?.Deserialize<UdlDemoModuleDefinitionDocument>(JsonOptions))
                .Where(static document => document is not null)
                .Select(document => FromDocument(document!));

            return SerializeDefinitions(definitions);
        }

        return node?.GetValue<string>() ?? string.Empty;
    }

    public static List<UdlDemoModuleDefinitionDocument> ToDocuments(string? rawDefinitions)
    {
        return ParseDefinitions(rawDefinitions)
            .Select(ToDocument)
            .ToList();
    }

    public static string FromDocuments(IEnumerable<UdlDemoModuleDefinitionDocument>? documents)
    {
        if (documents is null)
        {
            return string.Empty;
        }

        return SerializeDefinitions(documents
            .Where(static document => document is not null)
            .Select(document => FromDocument(document!)));
    }

    private static UdlDemoModuleDefinitionDocument ToDocument(UdlDemoModuleDefinition definition)
    {
        return new UdlDemoModuleDefinitionDocument
        {
            Name = definition.Name,
            Kind = definition.Kind,
            Generator = definition.Generator,
            Unit = definition.Unit,
            Format = definition.Format,
            BaseValue = definition.BaseValue,
            Amplitude = definition.Amplitude,
            PeriodSeconds = definition.PeriodSeconds,
            InitialValue = definition.InitialValue,
            SetScale = definition.SetScale,
            SetOffset = definition.SetOffset,
            SetTauSeconds = definition.SetTauSeconds,
            Faults = definition.Faults
                .Where(static fault => fault is not null)
                .Select(static fault => new UdlDemoFaultDefinitionDocument
                {
                    Kind = fault.Kind,
                    Enabled = fault.Enabled,
                    Amount = fault.Amount,
                    PeakAmount = fault.PeakAmount,
                    UseJitter = fault.UseJitter,
                    UseSine = fault.UseSine,
                    UsePeak = fault.UsePeak,
                    NoiseMode = fault.NoiseMode,
                    PeriodSeconds = fault.PeriodSeconds,
                    UpdateIntervalMs = fault.UpdateIntervalMs,
                    IntervalSeconds = fault.IntervalSeconds,
                    DurationMs = fault.DurationMs
                })
                .ToList()
        };
    }

    private static UdlDemoModuleDefinition FromDocument(UdlDemoModuleDefinitionDocument document)
    {
        return NormalizeDefinition(new UdlDemoModuleDefinition
        {
            Name = document.Name,
            Kind = document.Kind,
            Generator = document.Generator,
            Unit = document.Unit,
            Format = document.Format,
            BaseValue = document.BaseValue,
            Amplitude = document.Amplitude,
            PeriodSeconds = document.PeriodSeconds,
            InitialValue = document.InitialValue,
            SetScale = document.SetScale,
            SetOffset = document.SetOffset,
            SetTauSeconds = document.SetTauSeconds,
            Faults = document.Faults
                .Where(static fault => fault is not null)
                .Select(static fault => new UdlDemoFaultDefinition
                {
                    Kind = fault.Kind,
                    Enabled = fault.Enabled,
                    Amount = fault.Amount,
                    PeakAmount = fault.PeakAmount,
                    UseJitter = fault.UseJitter,
                    UseSine = fault.UseSine,
                    UsePeak = fault.UsePeak,
                    NoiseMode = fault.NoiseMode,
                    PeriodSeconds = fault.PeriodSeconds,
                    UpdateIntervalMs = fault.UpdateIntervalMs,
                    IntervalSeconds = fault.IntervalSeconds,
                    DurationMs = fault.DurationMs
                })
                .ToList()
        });
    }

    private static UdlDemoModuleDefinition NormalizeDefinition(UdlDemoModuleDefinition definition)
    {
        return definition;
    }
}
