using Tamp;
using Tamp.NetCli.V10;

/// <summary>
/// tamp-ef's self-hosted build script. Packs the three sibling EF Core
/// wrappers (V8 / V9 / V10). Unit tests run in CI; integration tests
/// require dotnet-ef on PATH and are run separately (locally / via a
/// dedicated job).
/// </summary>
class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter("Build configuration")]
    Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Package version override", EnvironmentVariable = "PACKAGE_VERSION")]
#pragma warning disable CS0649
    readonly string? Version;
#pragma warning restore CS0649

    [Solution] readonly Solution Solution = null!;
    [GitRepository] readonly GitRepository Git = null!;

    static readonly Secret? NuGetApiKey =
        Environment.GetEnvironmentVariable("NUGET_API_KEY") is { Length: > 0 } v
            ? new Secret("NuGet API key", v)
            : null;

    AbsolutePath Artifacts => RootDirectory / "artifacts";

    Target Info => _ => _.Executes(() =>
    {
        Console.WriteLine($"  Branch:        {Git.Branch ?? "<detached>"}");
        Console.WriteLine($"  Commit:        {Git.Commit[..7]}");
        Console.WriteLine($"  Configuration: {Configuration}");
        Console.WriteLine($"  Solution:      {Solution.Name} ({Solution.Projects.Count} projects)");
    });

    Target Clean => _ => _
        .TopLevel()
        .Executes(() =>
        {
            foreach (var d in RootDirectory.GlobDirectories("**/bin", "**/obj")) d.Delete();
            Artifacts.Delete();
        });

    Target Restore => _ => _
        .Executes(() => DotNet.Restore(s => s.SetProject(Solution.Path)));

    Target Compile => _ => _
        .TopLevel()
        .DependsOn(nameof(Restore))
        .Executes(() => DotNet.Build(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoRestore(true)));

    Target Test => _ => _
        .TopLevel()
        .DependsOn(nameof(Compile))
        .Description("Unit tests across V8/V9/V10. Integration tests run separately (need dotnet-ef on PATH).")
        .Executes(() => new[]
        {
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V8.Tests" / "Tamp.EFCore.V8.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v8.trx")
                .SetResultsDirectory(Artifacts / "test-results")),
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V9.Tests" / "Tamp.EFCore.V9.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v9.trx")
                .SetResultsDirectory(Artifacts / "test-results")),
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V10.Tests" / "Tamp.EFCore.V10.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v10.trx")
                .SetResultsDirectory(Artifacts / "test-results")),
        });

    Target Pack => _ => _
        .TopLevel()
        .DependsOn(nameof(Test))
        .Description("Pack the three EF Core wrapper packages.")
        .Executes(() => new[]
        {
            DotNet.Pack(s =>
            {
                s.SetProject(RootDirectory / "src" / "Tamp.EFCore.V8" / "Tamp.EFCore.V8.csproj");
                s.SetConfiguration(Configuration);
                s.SetNoBuild(true);
                s.SetOutput(Artifacts);
                if (!string.IsNullOrEmpty(Version)) s.SetProperty("Version", Version);
            }),
            DotNet.Pack(s =>
            {
                s.SetProject(RootDirectory / "src" / "Tamp.EFCore.V9" / "Tamp.EFCore.V9.csproj");
                s.SetConfiguration(Configuration);
                s.SetNoBuild(true);
                s.SetOutput(Artifacts);
                if (!string.IsNullOrEmpty(Version)) s.SetProperty("Version", Version);
            }),
            DotNet.Pack(s =>
            {
                s.SetProject(RootDirectory / "src" / "Tamp.EFCore.V10" / "Tamp.EFCore.V10.csproj");
                s.SetConfiguration(Configuration);
                s.SetNoBuild(true);
                s.SetOutput(Artifacts);
                if (!string.IsNullOrEmpty(Version)) s.SetProperty("Version", Version);
            }),
        });

    Target Push => _ => _
        .TopLevel()
        .DependsOn(nameof(Pack))
        .Requires(() => NuGetApiKey != null)
        .Executes(() => Artifacts.GlobFiles("*.nupkg")
            .Select(p => DotNet.NuGetPush(s => s
                .SetPackagePath(p)
                .SetSource("https://api.nuget.org/v3/index.json")
                .SetApiKey(NuGetApiKey!)
                .SetSkipDuplicate(true))));

    Target Ci => _ => _
        .TopLevel()
        .DependsOn(nameof(Info), nameof(Clean), nameof(Pack));

    Target Default => _ => _.DependsOn(nameof(Compile));
}
