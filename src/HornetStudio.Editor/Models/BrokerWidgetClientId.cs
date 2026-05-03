using System;

namespace HornetStudio.Editor.Models;

internal static class BrokerWidgetClientId
{
    private const string Prefix = $"{BrokerWidgetDefaults.ClientIdDisplay}-";

    internal static string Create()
        => $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 8)];

    internal static string Normalize(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Create();
        }

        var normalized = clientId.Trim();
        return normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : Create();
    }
}
