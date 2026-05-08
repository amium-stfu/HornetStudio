using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HornetStudio.Host;

public sealed class ProjectRoslynCompiler
{
    public static Func<IReadOnlyList<string>> AdditionalReferenceResolver { get; set; } = static () => Array.Empty<string>();
    public static Action<string>? InfoLogger { get; set; }
    public static Action<string>? DebugLogger { get; set; }

    private readonly object _buildLock = new();
    private AssemblyLoadContext? _loadContext;
    private WeakReference? _loadContextWeakReference;

    public async Task<ProjectBuildResult> BuildAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var project = ProjectLoader.Load(rootDirectory);
        return await BuildAsync(project, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectBuildResult> BuildAsync(ProjectModel project, CancellationToken cancellationToken = default)
    {
        if (project.SourceFiles.Count == 0)
        {
            throw new InvalidOperationException($"No C# source files found in '{project.RootDirectory}'.");
        }

        lock (_buildLock)
        {
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithNullableContextOptions(NullableContextOptions.Enable);
        var syntaxTrees = new List<SyntaxTree>(project.SourceFiles.Count);

        foreach (var sourceFile in project.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullSourceFile = Path.GetFullPath(sourceFile);
            var text = await File.ReadAllTextAsync(fullSourceFile, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var sourceText = SourceText.From(text, Encoding.UTF8);
            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                parseOptions,
                path: fullSourceFile,
                cancellationToken: cancellationToken);

            syntaxTrees.Add(syntaxTree);
        }
        var compilation = CSharpCompilation.Create(
            project.AssemblyName,
            syntaxTrees,
            GetMetadataReferences(),
            compilationOptions);

        var outputDirectory = project.RootDirectory;
        Directory.CreateDirectory(outputDirectory);

        var dllPath = Path.Combine(outputDirectory, "project.dll");
        var pdbPath = Path.Combine(outputDirectory, "project.pdb");

        await using var peStream = new MemoryStream();
        await using var pdbStream = new MemoryStream();

        var emitResult = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: pdbPath),
            cancellationToken: cancellationToken);

        var diagnostics = emitResult.Diagnostics
            .OrderByDescending(static d => d.Severity)
            .ThenBy(static d => d.Location.SourceTree?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static d => d.Location.SourceSpan.Start)
            .ToImmutableArray();

        if (!emitResult.Success)
        {
            return new ProjectBuildResult(project, false, diagnostics, null, dllPath, pdbPath, null, null);
        }

        peStream.Position = 0;
        pdbStream.Position = 0;

        var peImage = peStream.ToArray();
        var pdbImage = pdbStream.ToArray();

        await File.WriteAllBytesAsync(dllPath, peImage, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(pdbPath, pdbImage, cancellationToken).ConfigureAwait(false);

        var assembly = LoadAssembly(dllPath);
        var sourcePaths = compilation.SyntaxTrees
            .Select(static tree => tree.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var firstPdbDocument = TryReadFirstPdbDocument(pdbImage);
        InfoLogger?.Invoke(
            $"Build output ready. AssemblyPath={dllPath} PdbPath={pdbPath} SourceFileCount={sourcePaths.Length} FirstSource={sourcePaths.FirstOrDefault() ?? "<none>"}");
        InfoLogger?.Invoke($"PDB first document={firstPdbDocument ?? "<unavailable>"}");

        return new ProjectBuildResult(project, true, diagnostics, assembly, dllPath, pdbPath, peImage, pdbImage);
    }

    public Assembly LoadRuntimeAssembly(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        InfoLogger?.Invoke($"LoadRuntimeAssembly requested for {assemblyPath}");
        return LoadAssembly(assemblyPath);
    }

    public void UnloadRuntimeAssembly()
    {
        lock (_buildLock)
        {
            UnloadPreviousAssembly();
        }
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();

        foreach (var assemblyPath in trustedPlatformAssemblies)
        {
            TryAddReference(references, assemblyPath);
        }

        foreach (var assemblyPath in AdditionalReferenceResolver())
        {
            TryAddReference(references, assemblyPath);
        }

        return references.Values.ToArray();
    }

    private static void TryAddReference(IDictionary<string, MetadataReference> references, string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            return;
        }

        if (references.ContainsKey(assemblyPath))
        {
            return;
        }

        references[assemblyPath] = MetadataReference.CreateFromFile(assemblyPath);
    }

    private Assembly LoadAssembly(string assemblyPath)
    {
        UnloadPreviousAssembly();

        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        InfoLogger?.Invoke($"Loading runtime assembly from path {fullAssemblyPath}");

        var loadContext = new ProjectRuntimeLoadContext(fullAssemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(fullAssemblyPath);

        InfoLogger?.Invoke(
            $"Runtime assembly loaded. FullName={assembly.FullName} Location={assembly.Location} LoadContext={loadContext.Name}");

        _loadContext = loadContext;
        _loadContextWeakReference = new WeakReference(loadContext, trackResurrection: false);
        return assembly;
    }

    private sealed class ProjectRuntimeLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ProjectRuntimeLoadContext(string mainAssemblyPath)
            : base($"project-runtime-{DateTime.UtcNow:yyyyMMddHHmmssfff}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly =>
                    !assembly.IsDynamic
                    && string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

            if (sharedAssembly is not null)
            {
                return sharedAssembly;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return null;
            }

            return LoadFromAssemblyPath(assemblyPath);
        }
    }

    private void UnloadPreviousAssembly()
    {
        if (_loadContext is null)
        {
            return;
        }

        _loadContext.Unload();
        _loadContext = null;

        if (_loadContextWeakReference is null)
        {
            return;
        }

        for (var i = 0; _loadContextWeakReference.IsAlive && i < 8; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static string? TryReadFirstPdbDocument(byte[] pdbImage)
    {
        try
        {
            using var pdbStream = new MemoryStream(pdbImage, writable: false);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            var reader = provider.GetMetadataReader();
            if (reader.Documents.Count == 0)
            {
                return null;
            }

            var document = reader.GetDocument(reader.Documents.First());
            return reader.GetString(document.Name);
        }
        catch (Exception ex)
        {
            DebugLogger?.Invoke($"Reading first PDB document failed: {ex.Message}");
            return null;
        }
    }
}

public sealed class ProjectBuildResult
{
    public ProjectBuildResult(
        ProjectModel project,
        bool success,
        ImmutableArray<Diagnostic> diagnostics,
        Assembly? assembly,
        string assemblyPath,
        string pdbPath,
        byte[]? assemblyImage,
        byte[]? pdbImage)
    {
        Project = project;
        Success = success;
        Diagnostics = diagnostics;
        Assembly = assembly;
        AssemblyPath = assemblyPath;
        PdbPath = pdbPath;
        AssemblyImage = assemblyImage;
        PdbImage = pdbImage;
    }

    public ProjectModel Project { get; }
    public bool Success { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public Assembly? Assembly { get; }
    public string AssemblyPath { get; }
    public string PdbPath { get; }
    public byte[]? AssemblyImage { get; }
    public byte[]? PdbImage { get; }

    public int ErrorCount => Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning);
}

