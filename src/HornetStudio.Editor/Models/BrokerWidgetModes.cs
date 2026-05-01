namespace HornetStudio.Editor.Models;

/// <summary>
/// Defines the supported BrokerWidget connection modes.
/// </summary>
public static class BrokerWidgetModes
{
    /// <summary>
    /// Connects to an externally managed MQTT ItemBroker endpoint.
    /// </summary>
    public const string External = "External";

    /// <summary>
    /// Starts an in-process ItemBroker with MQTT adapter owned by the widget.
    /// </summary>
    public const string Own = "Own";

    /// <summary>
    /// Normalizes a persisted BrokerWidget mode.
    /// </summary>
    /// <param name="value">The persisted mode value.</param>
    /// <returns>The normalized mode value.</returns>
    public static string Normalize(string? value)
        => string.Equals(value?.Trim(), Own, StringComparison.OrdinalIgnoreCase)
            ? Own
            : External;
}
