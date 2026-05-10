using System.IO;
using Tamp;

namespace Tamp.EFCore.V10.IntegrationTests;

/// <summary>
/// Stages an isolated copy of <c>samples/SampleEfApp</c> in a temp dir
/// for one test, builds it once, exposes the paths the EF verbs need,
/// and cleans up on dispose.
/// </summary>
internal sealed class SampleApp : IDisposable
{
    public AbsolutePath Root { get; }
    public AbsolutePath ProjectFile => Root / "SampleEfApp.csproj";
    public AbsolutePath ConnectionDbFile => Root / "test.db";
    public string ConnectionString => $"Data Source={ConnectionDbFile.Value}";
    public Tool Ef { get; }

    private SampleApp(AbsolutePath root, Tool ef) { Root = root; Ef = ef; }

    public static SampleApp Stage()
    {
        // Walk up from the test assembly to find the repo root.
        var dir = AbsolutePath.Create(AppContext.BaseDirectory);
        while (dir.DirectoryExists() && !(dir / "Tamp.EF.slnx").FileExists())
        {
            var parent = dir.Parent;
            if (parent is null || parent.Value == dir.Value)
                throw new InvalidOperationException(
                    $"Could not locate Tamp.EF.slnx walking up from {AppContext.BaseDirectory}.");
            dir = parent;
        }
        var repoRoot = dir;

        var sourceSample = repoRoot / "samples" / "SampleEfApp";
        if (!sourceSample.DirectoryExists())
            throw new InvalidOperationException($"Sample app missing at {sourceSample}.");

        var ef = ResolveDotnetEf();

        var tempRoot = AbsolutePath.Create(Path.Combine(Path.GetTempPath(), $"tamp-ef-it-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(tempRoot.Value);

        // Copy *.cs and *.csproj only — leave bin/obj behind so the
        // staged copy gets a clean restore + build.
        foreach (var src in sourceSample.EnumerateFiles())
        {
            if (src.NameWithoutExtension is "obj" or "bin") continue;
            File.Copy(src.Value, Path.Combine(tempRoot.Value, src.Name));
        }

        // The staged copy needs a Directory.Build.props next to it that
        // doesn't import the parent repo's MSBuild plumbing — otherwise
        // it tries to resolve TampCorePath from a directory that
        // doesn't exist relative to the temp location.
        File.WriteAllText(Path.Combine(tempRoot.Value, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(tempRoot.Value, "Directory.Packages.props"),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

        // Pin EF package versions inline since we just disabled central management.
        var csprojPath = Path.Combine(tempRoot.Value, "SampleEfApp.csproj");
        var csproj = File.ReadAllText(csprojPath);
        csproj = csproj
            .Replace("Include=\"Microsoft.EntityFrameworkCore.Sqlite\"",
                     "Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.7\"")
            .Replace("Include=\"Microsoft.EntityFrameworkCore.Design\"",
                     "Include=\"Microsoft.EntityFrameworkCore.Design\" Version=\"10.0.7\"");
        File.WriteAllText(csprojPath, csproj);

        // Pre-build so subsequent ef invocations can pass --no-build.
        var build = ProcessRunner.Capture(new CommandPlan
        {
            Executable = "dotnet",
            Arguments = new[] { "build", csprojPath, "-nologo", "-v:q" },
        });
        if (build.Failed)
        {
            try { Directory.Delete(tempRoot.Value, recursive: true); } catch { }
            throw new InvalidOperationException(
                $"Sample build failed (exit {build.ExitCode}). Output:\n{build.StderrText}\n{build.StdoutText}");
        }

        return new SampleApp(tempRoot, ef);
    }

    private static Tool ResolveDotnetEf()
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        var candidates = new[]
        {
            Path.Combine(home, ".dotnet", "tools", "dotnet-ef"),
            Path.Combine(home, ".dotnet", "tools", "dotnet-ef.exe"),
            "/usr/local/bin/dotnet-ef",
        };
        foreach (var c in candidates)
            if (File.Exists(c))
                return new Tool(AbsolutePath.Create(c));

        throw new InvalidOperationException(
            "dotnet-ef not found in any expected location. Install with: dotnet tool install -g dotnet-ef --version 10.*");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root.Value, recursive: true); } catch { /* best effort */ }
    }
}
