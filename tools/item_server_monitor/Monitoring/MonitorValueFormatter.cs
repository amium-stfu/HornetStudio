using System.Globalization;

namespace Item.Server.Monitor.Monitoring;

internal static class MonitorValueFormatter
{
    public static string Format(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value switch
        {
            string text => string.IsNullOrWhiteSpace(text) ? "\"\"" : text,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}