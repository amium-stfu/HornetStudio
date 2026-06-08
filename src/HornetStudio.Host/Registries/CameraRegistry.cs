using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HornetStudio.Host.Registries;

public interface ICameraFrameSource
{
    string Name { get; }
    object? CurrentFrame { get; }
    IReadOnlyCollection<string> SupportedResolutions { get; }
    void SetResolution(string? resolutionLabel);
    event EventHandler? FrameAvailable;
}

public interface ICameraRegistry
{
    IReadOnlyCollection<ICameraFrameSource> GetAll();
    void Register(ICameraFrameSource source);
    bool TryGet(string name, out ICameraFrameSource? source);
}

public sealed class CameraRegistry : ICameraRegistry
{
    private readonly ConcurrentDictionary<string, ICameraFrameSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ICameraFrameSource> GetAll() => _sources.Values.OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(ICameraFrameSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new ArgumentException("Camera source name must not be empty.", nameof(source));
        }

        _sources[source.Name] = source;
    }

    public bool TryGet(string name, out ICameraFrameSource? source) => _sources.TryGetValue(name, out source);
}
