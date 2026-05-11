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

    // Bound by SecretBinder from NUGET_API_KEY env var (TAM-78,

    // Tamp.Core 1.0.1). CI masking via TampBuild.RegisterSecretForCiMasking.

    [Secret("NuGet API key", EnvironmentVariable = "NUGET_API_KEY")]

    readonly Secret NuGetApiKey = null!;

    AbsolutePath Artifacts => RootDirectory / "artifacts";

    Target Info => _ => _.Executes(() =>
    {
        Console.WriteLine($"  Branch:        {Git.Branch ?? "<detached>"}");
        Console.WriteLine($"  Commit:        {Git.Commit[..7]}");
        Console.WriteLine($"  Configuration: {Configuration}");
    });

    Target Clean => _ => _
        .Executes(() =>
        {
            // Exclude the build script's own bin/obj — we're currently running from there.
            // Deleting them mid-run would self-evict the Tamp.NetCli.V10 dll the Restore
            // target needs. The Tamp.Core 1.0.8 GlobDirectories fix surfaced this trap.
            var buildDir = (RootDirectory / "build").Value;
            foreach (var d in RootDirectory.GlobDirectories("**/bin", "**/obj"))
            {
                if (d.Value.StartsWith(buildDir, StringComparison.Ordinal)) continue;
                d.Delete();
            }
            Artifacts.Delete();
        });

    Target Restore => _ => _
        .Executes(() => DotNet.Restore(s => s.SetProject(Solution.Path)));

    Target Compile => _ => _
        .DependsOn(nameof(Restore))
        .Executes(() => DotNet.Build(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoRestore(true)));

    Target Test => _ => _
        .DependsOn(nameof(Compile))
        .Description("Unit tests across V8/V9/V10. Integration tests run separately (need dotnet-ef on PATH).")
        .Executes(() => new[]
        {
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V8.Tests" / "Tamp.EFCore.V8.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v8.trx")
                .AddDataCollector("XPlat Code Coverage")
                .SetSettings((RootDirectory / "build" / "coverlet.runsettings").Value)
                .SetResultsDirectory(Artifacts / "test-results")),
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V9.Tests" / "Tamp.EFCore.V9.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v9.trx")
                .AddDataCollector("XPlat Code Coverage")
                .SetSettings((RootDirectory / "build" / "coverlet.runsettings").Value)
                .SetResultsDirectory(Artifacts / "test-results")),
            DotNet.Test(s => s
                .SetProject(RootDirectory / "tests" / "Tamp.EFCore.V10.Tests" / "Tamp.EFCore.V10.Tests.csproj")
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .AddLogger("trx;LogFileName=efcore-v10.trx")
                .AddDataCollector("XPlat Code Coverage")
                .SetSettings((RootDirectory / "build" / "coverlet.runsettings").Value)
                .SetResultsDirectory(Artifacts / "test-results")),
        });

    Target Pack => _ => _
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
        .DependsOn(nameof(Pack))
        .Requires(() => NuGetApiKey != null)
        .Executes(() => Artifacts.GlobFiles("*.nupkg")
            .Select(p => DotNet.NuGetPush(s => s
                .SetPackagePath(p)
                .SetSource("https://api.nuget.org/v3/index.json")
                .SetApiKey(NuGetApiKey)
                .SetSkipDuplicate(true))));

    Target Ci => _ => _
        .DependsOn(nameof(Info), nameof(Clean), nameof(Pack));

    Target Default => _ => _.DependsOn(nameof(Compile));

    // ----- Sonar (TAM-17) -----

    [NuGetPackage("dotnet-sonarscanner", Version = "10.4.1")]
    readonly Tool SonarTool = null!;


    [Secret("SonarQube token", EnvironmentVariable = "SONAR_TOKEN")]


    readonly Secret SonarToken = null!;

    [Parameter("Sonar host URL", EnvironmentVariable = "SONAR_HOST_URL")]
    readonly string SonarHostUrl = "https://sonar.brewingcoder.com";

    [Parameter("Sonar project key")]
    readonly string SonarProjectKey = "tamp-build_tamp-ef";

    Target SonarBegin => _ => _
        .Description("Initialize the SonarScanner pre-build phase.")
        .Before(nameof(Compile))
        .Requires(() => SonarToken != null)
        .Executes(() => Tamp.SonarScanner.V10.SonarScanner.Begin(SonarTool, s =>
        {
            s.SetProjectKey(SonarProjectKey);
            s.SetHostUrl(SonarHostUrl);
            s.SetToken(SonarToken);
            s.SetProperty("sonar.cs.vstest.reportsPaths", $"{(Artifacts / "test-results").Value}/**/*.trx");
            s.SetProperty("sonar.cs.opencover.reportsPaths", $"{(Artifacts / "test-results").Value}/**/coverage.opencover.xml");

            s.SetProperty("sonar.coverage.exclusions", "tests/**,build/**,samples/**");

            s.SetProperty("sonar.exclusions", "**/bin/**,**/obj/**,artifacts/**,build/**,docs/**,samples/**");
            s.SetProperty("sonar.cpd.exclusions", "src/Tamp.EFCore.V8/**,src/Tamp.EFCore.V9/**");
        }));

    Target SonarEnd => _ => _
        .Description("Finalize SonarScanner and submit results to the server.")
        .DependsOn(nameof(Test))
        .Requires(() => SonarToken != null)
        .Executes(() => Tamp.SonarScanner.V10.SonarScanner.End(SonarTool, s => s.SetToken(SonarToken)));

    Target Sonar => _ => _
        .DependsOn(nameof(SonarBegin), nameof(SonarEnd))
        .Description("End-to-end Sonar scan: Begin (before Compile) → Compile → Test → End. Requires SONAR_TOKEN.");

}
