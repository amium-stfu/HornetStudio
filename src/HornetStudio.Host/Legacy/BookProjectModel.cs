using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HornetStudio.Host;

public sealed class ProjectModel
{
    public ProjectModel(
        string rootDirectory,
        string projectName,
        string assemblyName,
        string targetFramework,
        string foldersDirectory,
        string programFile,
        string globalUsingsFile,
        string projectFile,
        IReadOnlyList<ProjectFolder> folders,
        IReadOnlyList<string> uiFiles,
        IReadOnlyList<string> sourceFiles)
    {
        RootDirectory = rootDirectory;
        ProjectName = projectName;
        AssemblyName = assemblyName;
        TargetFramework = targetFramework;
        FoldersDirectory = foldersDirectory;
        ProgramFile = programFile;
        GlobalUsingsFile = globalUsingsFile;
        ProjectFile = projectFile;
        Folders = folders;
        UiFiles = uiFiles;
        SourceFiles = sourceFiles;
    }

    public string RootDirectory { get; }
    public string ProjectName { get; }
    public string AssemblyName { get; }
    public string TargetFramework { get; }
    public string FoldersDirectory { get; }
    public string ProgramFile { get; }
    public string GlobalUsingsFile { get; }
    public string ProjectFile { get; }
    public IReadOnlyList<ProjectFolder> Folders { get; }
    public IReadOnlyList<string> UiFiles { get; }
    public IReadOnlyList<string> SourceFiles { get; }
}

public sealed class ProjectFolder
{
    public ProjectFolder(string name, string directory, string? metadataFile, string? uiFile, IReadOnlyList<string> sourceFiles)
    {
        Name = name;
        Directory = directory;
        MetadataFile = metadataFile;
        UiFile = uiFile;
        SourceFiles = sourceFiles;
    }

    public string Name { get; }
    public string Directory { get; }
    public string? MetadataFile { get; }
    public string? UiFile { get; }
    public IReadOnlyList<string> SourceFiles { get; }
}

public sealed class ProjectManifest
{
    public string ProjectName { get; set; } = "Project";
    public string? TargetFramework { get; set; }
}

public static class ProjectLoader
{
    public static Func<string, IReadOnlyList<string>> ReferencePathResolver { get; set; } = static _ => Array.Empty<string>();

    public static ProjectModel Load(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory must not be empty.", nameof(rootDirectory));
        }

        var fullRoot = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(fullRoot);
        }

        var manifest = ReadManifest(fullRoot);
        var projectName = !string.IsNullOrWhiteSpace(manifest?.ProjectName)
            ? manifest!.ProjectName
            : Path.GetFileName(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var targetFramework = !string.IsNullOrWhiteSpace(manifest?.TargetFramework)
            ? manifest!.TargetFramework!
            : "net9.0-windows";

        var foldersDirectory = Path.Combine(fullRoot, "Folders");
        if (!Directory.Exists(foldersDirectory))
        {
            foldersDirectory = Path.Combine(fullRoot, "Pages");
        }

        if (!Directory.Exists(foldersDirectory))
        {
            throw new DirectoryNotFoundException($"Folders directory not found: {foldersDirectory}");
        }

        var programFile = Path.Combine(fullRoot, "Program.cs");
        var globalUsingsFile = Path.Combine(fullRoot, "GlobalUsing.cs");
        var fallbackGlobalUsingsFile = Path.Combine(fullRoot, "GlobalUsings.cs");
        if (!File.Exists(globalUsingsFile) && File.Exists(fallbackGlobalUsingsFile))
        {
            globalUsingsFile = fallbackGlobalUsingsFile;
        }

        var projectFile = Path.Combine(fullRoot, projectName + ".csproj");
        var folders = LoadFolders(foldersDirectory);
        var uiFiles = folders
            .Select(folder => folder.UiFile)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        var sourceFiles = CollectSourceFiles(fullRoot, foldersDirectory, programFile, globalUsingsFile, folders);

        var project = new ProjectModel(
            fullRoot,
            projectName,
            projectName,
            targetFramework,
            foldersDirectory,
            programFile,
            globalUsingsFile,
            projectFile,
            folders,
            uiFiles,
            sourceFiles);

        EnsureProjectFile(project);
        return project;
    }

    private static ProjectManifest? ReadManifest(string rootDirectory)
    {
        var manifestPath = Path.Combine(rootDirectory, "project.json");
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(rootDirectory, "Book.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }
        }

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<ProjectManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static IReadOnlyList<ProjectFolder> LoadFolders(string foldersDirectory)
    {
        var folders = new List<ProjectFolder>();
        foreach (var folderDirectory in Directory.GetDirectories(foldersDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(folderDirectory);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var metadataFile = Path.Combine(folderDirectory, "oFolder.json");
            if (!File.Exists(metadataFile))
            {
                metadataFile = Path.Combine(folderDirectory, "oPage.json");
                if (!File.Exists(metadataFile))
                {
                    metadataFile = null;
                }
            }

            var uiFile = Path.Combine(folderDirectory, "Folder.json");
            if (!File.Exists(uiFile))
            {
                uiFile = Path.Combine(folderDirectory, "Page.json");
                if (!File.Exists(uiFile))
                {
                    uiFile = null;
                }
            }

            var sourceFiles = Directory
                .EnumerateFiles(folderDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsIgnoredPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            folders.Add(new ProjectFolder(folderName, folderDirectory, metadataFile, uiFile, sourceFiles));
        }

        return folders;
    }

    private static IReadOnlyList<string> CollectSourceFiles(
        string rootDirectory,
        string foldersDirectory,
        string programFile,
        string globalUsingsFile,
        IReadOnlyList<ProjectFolder> folders)
    {
        var files = new List<string>();

        if (File.Exists(programFile))
        {
            files.Add(programFile);
        }

        if (File.Exists(globalUsingsFile))
        {
            files.Add(globalUsingsFile);
        }

        files.AddRange(folders.SelectMany(folder => folder.SourceFiles));

        files.AddRange(
            Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    !path.Equals(programFile, StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals(globalUsingsFile, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        return files
            .Where(path => !IsIgnoredPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsIgnoredPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.vscode{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureProjectFile(ProjectModel project)
    {
        Directory.CreateDirectory(project.RootDirectory);
        var content = CreateProjectFileContent(project);
        File.WriteAllText(project.ProjectFile, content, Encoding.UTF8);
    }

    private static string CreateProjectFileContent(ProjectModel project)
    {
        var references = ReferencePathResolver(project.RootDirectory);

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine($"    <TargetFramework>{EscapeXml(project.TargetFramework)}</TargetFramework>");
        sb.AppendLine($"    <AssemblyName>{EscapeXml(project.AssemblyName)}</AssemblyName>");
        sb.AppendLine("    <RootNamespace>QB</RootNamespace>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <LangVersion>preview</LangVersion>");
        sb.AppendLine("    <DebugType>portable</DebugType>");
        sb.AppendLine("    <DebugSymbols>true</DebugSymbols>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("  </PropertyGroup>");

        sb.AppendLine("  <ItemGroup>");
        foreach (var sourceFile in project.SourceFiles
                     .Select(path => Path.GetRelativePath(project.RootDirectory, path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    <Compile Include=\"{EscapeXml(sourceFile)}\" />");
        }

        sb.AppendLine("  </ItemGroup>");

        var contentFiles = project.UiFiles
            .Append(Path.Combine(project.RootDirectory, "project.json"))
            .Append(Path.Combine(project.RootDirectory, "Book.json"))
            .Append(Path.Combine(project.RootDirectory, "pipes.json"))
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(project.RootDirectory, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (contentFiles.Length > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var contentFile in contentFiles)
            {
                sb.AppendLine($"    <None Include=\"{EscapeXml(contentFile)}\" />");
            }

            sb.AppendLine("  </ItemGroup>");
        }

        if (references.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var referencePath in references)
            {
                var include = Path.GetFileNameWithoutExtension(referencePath);
                sb.AppendLine($"    <Reference Include=\"{EscapeXml(include)}\">");
                sb.AppendLine($"      <HintPath>{EscapeXml(referencePath)}</HintPath>");
                sb.AppendLine("      <Private>false</Private>");
                sb.AppendLine("    </Reference>");
            }

            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}


