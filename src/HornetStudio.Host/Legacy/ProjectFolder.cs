using System;
using HornetStudio.Logging;
using Amium.Items;

namespace HornetStudio.Host;

public abstract class ProjectFolderBase : IDisposable
{
    private readonly UiFolderContext _context;
    private bool _initialized;
    private bool _running;
    private bool _disposed;

    protected ProjectFolderBase(string folderName, string? projectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        FolderName = folderName.Trim();
        ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim();
        _context = new UiFolderContext(FolderName, ProjectName);
    }

    protected string FolderName { get; }
    protected string? ProjectName { get; }

    /// <summary>
    /// Attaches a source item to the current folder context and returns the folder-bound item instance.
    /// </summary>
    /// <param name="source">The source item that should be attached to this folder.</param>
    /// <param name="alias">An optional folder-local path segment used instead of the source item name.</param>
    /// <returns>The attached item instance bound to the current folder context.</returns>
    protected Item Attach(Item source, string? alias = null)
        => _context.Attach(source, alias);

    /// <summary>
    /// Creates a folder-scoped command using the convention &lt;Folder&gt;/Commands/&lt;name&gt;.
    /// </summary>
    /// <param name="name">The folder-local command name.</param>
    /// <param name="action">The command callback.</param>
    /// <param name="description">An optional description for editor selection and documentation.</param>
    /// <returns>A host command with the generated folder-scoped command path.</returns>
    protected HostCommand CreateCommand(string name, Action action, string? description = null)
        => _context.CreateCommand(name, action, description);

    /// <summary>
    /// Creates a folder-scoped command using the legacy attach naming.
    /// </summary>
    protected HostCommand AttachCommand(string name, Action action, string? description = null)
        => CreateCommand(name, action, description);

    /// <summary>
    /// Publishes an item to the UI. Raw source items are attached to this folder automatically, while already folder-bound items are published as-is.
    /// </summary>
    protected Item PublishItem(Item item, string? alias = null, bool pruneMissingMembers = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var folderItem = IsFolderScoped(item.Path) && string.IsNullOrWhiteSpace(alias)
            ? item
            : Attach(item, alias);

        return UiPublisher.Publish(folderItem, pruneMissingMembers);
    }

    /// <summary>
    /// Publishes a folder-scoped command to the command registry.
    /// </summary>
    protected HostCommand PublishCommand(string name, Action action, string? description = null)
    {
        var command = CreateCommand(name, action, description);
        UiPublisher.Publish(command);
        return command;
    }

    /// <summary>
    /// Publishes an already created command.
    /// </summary>
    protected HostCommand PublishCommand(HostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        UiPublisher.Publish(command);
        return command;
    }

    /// <summary>
    /// Publishes a process log below the current folder using the convention &lt;Folder&gt;/Logs/&lt;name&gt;.
    /// </summary>
    protected Item PublishProcessLog(string name, ProcessLog log, string? title = null, bool pruneMissingMembers = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(log);

        return UiPublisher.Publish(BuildFolderPath($"Logs/{name}"), log, title, pruneMissingMembers);
    }

    /// <summary>
    /// Publishes a camera source to the camera registry.
    /// </summary>
    protected ICameraFrameSource PublishCamera(ICameraFrameSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        UiPublisher.Publish(source);
        return source;
    }

    /// <summary>
    /// Initializes the folder and invokes the initialization hook exactly once.
    /// </summary>
    public void Initialize()
    {
        ThrowIfDisposed();
        if (_initialized)
        {
            return;
        }

        OnInitialize();
        _initialized = true;
    }

    /// <summary>
    /// Starts the folder lifecycle, ensuring initialization has completed beforehand.
    /// </summary>
    public void Run()
    {
        ThrowIfDisposed();
        if (!_initialized)
        {
            Initialize();
        }

        if (_running)
        {
            return;
        }

        OnRun();
        _running = true;
    }

    /// <summary>
    /// Stops the folder lifecycle and releases the folder context and attached resources.
    /// </summary>
    public void Destroy()
    {
        if (_disposed)
        {
            return;
        }

        if (_running)
        {
            OnDestroy();
            _running = false;
        }
        else if (_initialized)
        {
            OnDestroy();
        }

        _context.Dispose();
        _disposed = true;
        _initialized = false;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the folder by delegating to <see cref="Destroy"/>.
    /// </summary>
    public void Dispose()
        => Destroy();

    protected abstract void OnInitialize();
    protected abstract void OnRun();
    protected abstract void OnDestroy();

    protected string BuildFolderPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{_context.FolderPath}/{NormalizePath(relativePath)}";
    }

    private bool IsFolderScoped(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var normalizedFolderPath = NormalizePath(_context.FolderPath);
        return string.Equals(normalizedPath, normalizedFolderPath, StringComparison.Ordinal)
            || normalizedPath.StartsWith($"{normalizedFolderPath}/", StringComparison.Ordinal);
    }

    private static string NormalizePath(string value)
        => value.Replace('\\', '/').Trim('/');

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
