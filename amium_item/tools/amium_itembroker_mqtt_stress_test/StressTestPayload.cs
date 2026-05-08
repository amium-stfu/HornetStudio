using System.Diagnostics;
using System.Globalization;

namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Represents the compact textual stress test payload carried in the ItemBroker read value.
/// </summary>
/// <param name="RunId">The unique run identifier.</param>
/// <param name="Sequence">The per-signal sequence number.</param>
/// <param name="SentTimestamp">The local stopwatch timestamp captured before publishing.</param>
/// <param name="Value">The generated signal value.</param>
public sealed record StressTestPayload(string RunId, long Sequence, long SentTimestamp, double Value)
{
    private const string Prefix = "st1";

    /// <summary>
    /// Creates a payload with the current local stopwatch timestamp.
    /// </summary>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="sequence">The per-signal sequence number.</param>
    /// <param name="value">The generated value.</param>
    /// <returns>The payload.</returns>
    public static StressTestPayload Create(string runId, long sequence, double value)
        => new(
            RunId: runId,
            Sequence: sequence,
            SentTimestamp: Stopwatch.GetTimestamp(),
            Value: value);

    /// <summary>
    /// Formats the payload as compact non-JSON text.
    /// </summary>
    /// <returns>The formatted payload.</returns>
    public string Format()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix};run={RunId};seq={Sequence};sent={SentTimestamp};value={Value:R}");

    /// <summary>
    /// Tries to parse compact payload text.
    /// </summary>
    /// <param name="text">The payload text.</param>
    /// <param name="payload">The parsed payload when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string text, out StressTestPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string? runId = null;
        long? sequence = null;
        long? sentTimestamp = null;
        double? value = null;

        foreach (var part in parts.Skip(1))
        {
            var keyValue = part.Split('=', count: 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2)
            {
                return false;
            }

            switch (keyValue[0])
            {
                case "run":
                    runId = keyValue[1];
                    break;
                case "seq" when long.TryParse(keyValue[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSequence):
                    sequence = parsedSequence;
                    break;
                case "sent" when long.TryParse(keyValue[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimestamp):
                    sentTimestamp = parsedTimestamp;
                    break;
                case "value" when double.TryParse(keyValue[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue):
                    value = parsedValue;
                    break;
                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(runId) || sequence is null || sentTimestamp is null || value is null)
        {
            return false;
        }

        payload = new StressTestPayload(
            RunId: runId,
            Sequence: sequence.Value,
            SentTimestamp: sentTimestamp.Value,
            Value: value.Value);
        return true;
    }
}
