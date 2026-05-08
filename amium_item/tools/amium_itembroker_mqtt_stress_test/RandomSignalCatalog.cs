using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server.MqttStressTest;

/// <summary>
/// Creates deterministic stress signal definitions.
/// </summary>
public sealed class RandomSignalCatalog
{
    private readonly string _rootPath;
    private readonly Random _random;
    private readonly StressSignal[] _signals;

    /// <summary>
    /// Initializes a new instance of the <see cref="RandomSignalCatalog"/> class.
    /// </summary>
    /// <param name="rootPath">The stress item root path.</param>
    /// <param name="signalCount">The number of generated signals.</param>
    /// <param name="seed">The deterministic random seed.</param>
    public RandomSignalCatalog(string rootPath, int signalCount, int seed = 42)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (signalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(signalCount), "Signal count must be greater than zero.");
        }

        _rootPath = rootPath;
        _random = new Random(seed);
        _signals = Enumerable.Range(1, signalCount)
            .Select(CreateSignal)
            .ToArray();
    }

    /// <summary>
    /// Gets the generated stress signals.
    /// </summary>
    public IReadOnlyList<StressSignal> Signals => _signals;

    /// <summary>
    /// Returns a pseudo-random generated signal.
    /// </summary>
    /// <returns>A generated signal.</returns>
    public StressSignal NextSignal()
        => _signals[_random.Next(_signals.Length)];

    private StressSignal CreateSignal(int index)
    {
        var name = FormattableString.Invariant($"signal_{index:000000}");
        var path = string.Concat(_rootPath, ".", name);
        var initialValue = Math.Round(_random.NextDouble() * 100.0, 3);
        return new StressSignal(
            Path: path,
            ItemModel: new ItemModel(name: name, value: null, path: _rootPath),
            InitialValue: initialValue);
    }
}

/// <summary>
/// Describes one deterministic stress signal.
/// </summary>
/// <param name="Path">The normalized item path.</param>
/// <param name="ItemModel">The item instance used for publishing.</param>
/// <param name="InitialValue">The deterministic initial value.</param>
public sealed record StressSignal(string Path, ItemModel ItemModel, double InitialValue);
