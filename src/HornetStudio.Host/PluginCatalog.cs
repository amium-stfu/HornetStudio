using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HornetStudio.Contracts;

namespace HornetStudio.Host;

public sealed class LoadedPlugin
{
    public LoadedPlugin(
        PluginDescriptor descriptor,
        string assemblyPath,
        IReadOnlyList<DriverDescriptor> drivers,
        IReadOnlyList<string> referenceAssemblies)
    {
        Descriptor = descriptor;
        AssemblyPath = assemblyPath;
        Drivers = drivers;
        ReferenceAssemblies = referenceAssemblies;
    }

    public PluginDescriptor Descriptor { get; }
    public string AssemblyPath { get; }
    public IReadOnlyList<DriverDescriptor> Drivers { get; }
    public IReadOnlyList<string> ReferenceAssemblies { get; }
}

public static class HostPluginCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyList<LoadedPlugin> _plugins = Array.Empty<LoadedPlugin>();
    private static IReadOnlyList<string> _pluginReferenceAssemblies = Array.Empty<string>();
    private static bool _initialized;

    public static IReadOnlyList<LoadedPlugin> Plugins
    {
        get
        {
            EnsureLoaded();
            return _plugins;
        }
    }

    public static IReadOnlyList<string> GetProjectReferencePaths(string projectRoot)
    {
        EnsureLoaded();

        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var contractsAssemblyPath = Path.GetFullPath(typeof(IHostPlugin).Assembly.Location);
        if (File.Exists(contractsAssemblyPath) && ShouldIncludeReference(contractsAssemblyPath, projectRoot))
        {
            references.Add(contractsAssemblyPath);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(assembly.Location);
            if (!File.Exists(fullPath) || !ShouldIncludeReference(fullPath, projectRoot))
            {
                continue;
            }

            references.Add(fullPath);
        }

        foreach (var pluginReference in _pluginReferenceAssemblies)
        {
            if (File.Exists(pluginReference) && ShouldIncludeReference(pluginReference, projectRoot))
            {
                references.Add(pluginReference);
            }
        }

        return references
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _plugins = LoadPlugins();
            _pluginReferenceAssemblies = _plugins
                .SelectMany(static plugin => plugin.ReferenceAssemblies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _initialized = true;
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            _plugins = Array.Empty<LoadedPlugin>();
            _pluginReferenceAssemblies = Array.Empty<string>();
            _initialized = false;
        }
    }

    private static IReadOnlyList<LoadedPlugin> LoadPlugins()
    {
        var loadedPlugins = new List<LoadedPlugin>();

        foreach (var pluginDirectory in GetPluginDirectories())
        {
            foreach (var assemblyPath in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var pluginBaseDirectory = Path.GetDirectoryName(assemblyPath) ?? pluginDirectory;
                    var assembly = LoadPluginAssembly(assemblyPath);
                    if (assembly is null)
                    {
                        continue;
                    }

                    foreach (var pluginType in assembly
                                 .GetTypes()
                                 .Where(static type => typeof(IHostPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface))
                    {
                        if (Activator.CreateInstance(pluginType) is not IHostPlugin plugin)
                        {
                            continue;
                        }

                        var registration = new PluginRegistration(AppContext.BaseDirectory, pluginBaseDirectory, assemblyPath);
                        plugin.Register(registration);

                        loadedPlugins.Add(new LoadedPlugin(
                            plugin.Descriptor,
                            assemblyPath,
                            registration.Drivers,
                            registration.ReferenceAssemblies));

                        Core.LogInfo($"Plugin loaded: {plugin.Descriptor.Id} {plugin.Descriptor.Version} from {assemblyPath}");
                    }
                }
                catch (Exception ex)
                {
                    Core.LogWarn($"Plugin load failed for {assemblyPath}", ex);
                }
            }
        }

        return loadedPlugins;
    }

    private static Assembly? LoadPluginAssembly(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location)
                && string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static IReadOnlyList<string> GetPluginDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateProbeRoots(AppContext.BaseDirectory))
        {
            AddIfDirectoryExists(directories, Path.Combine(root, "Host", "plugins"));
            AddIfDirectoryExists(directories, Path.Combine(root, "plugins"));
        }

        foreach (var root in EnumerateProbeRoots(Environment.CurrentDirectory))
        {
            AddIfDirectoryExists(directories, Path.Combine(root, "Host", "plugins"));
            AddIfDirectoryExists(directories, Path.Combine(root, "plugins"));
        }

        return directories
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProbeRoots(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            yield break;
        }

        var fullPath = Path.GetFullPath(startPath);
        var current = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);

        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static void AddIfDirectoryExists(ISet<string> directories, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            directories.Add(fullPath);
        }
    }

    private static bool ShouldIncludeReference(string path, string projectRoot)
    {
        if (path.StartsWith(Path.GetFullPath(projectRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.Equals("project.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Book.dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dotnetRoot = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var sharedRuntime = Path.Combine(dotnetRoot, "dotnet", "shared");
        var referencePacks = Path.Combine(dotnetRoot, "dotnet", "packs");

        if (path.StartsWith(sharedRuntime + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith(referencePacks + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private sealed class PluginRegistration : IPluginRegistration
    {
        private readonly HashSet<string> _referenceAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DriverDescriptor> _drivers = new();

        public PluginRegistration(string hostDirectory, string pluginDirectory, string pluginAssemblyPath)
        {
            HostDirectory = hostDirectory;
            PluginDirectory = pluginDirectory;
            RegisterReferenceAssembly(pluginAssemblyPath);
        }

        public string HostDirectory { get; }
        public string PluginDirectory { get; }
        public IReadOnlyList<DriverDescriptor> Drivers => _drivers;
        public IReadOnlyList<string> ReferenceAssemblies => _referenceAssemblies.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        public void RegisterReferenceAssembly(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("Assembly path must not be empty.", nameof(assemblyPath));
            }

            var fullPath = Path.GetFullPath(assemblyPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Assembly reference not found.", fullPath);
            }

            _referenceAssemblies.Add(fullPath);
        }

        public void RegisterDriver(DriverDescriptor descriptor)
        {
            _drivers.Add(descriptor ?? throw new ArgumentNullException(nameof(descriptor)));
        }
    }
}


