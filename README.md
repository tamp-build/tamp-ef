# Tamp.EF

`dotnet ef` wrappers for [Tamp](https://github.com/tamp-build/tamp).

Three sibling packages, each pinned to an EF Core major:

| Package | EF Core | .NET runtime | Status |
|---|---|---|---|
| [`Tamp.EFCore.V8`](src/Tamp.EFCore.V8) | 8.x (LTS through Nov 2026) | net8.0 / net9.0 / net10.0 | preview |
| [`Tamp.EFCore.V9`](src/Tamp.EFCore.V9) | 9.x (STS through May 2026) | net8.0 / net9.0 / net10.0 | preview |
| [`Tamp.EFCore.V10`](src/Tamp.EFCore.V10) | 10.x (LTS through Nov 2028) | net8.0 / net9.0 / net10.0 | preview |

Pick the package that matches the EF Core major you're driving — not the
runtime your build script targets. The CLI surface (and its flags) is what
the `V` suffix pins, per [Tamp ADR 0002](https://github.com/tamp-build/tamp/blob/main/docs/adr/0002-package-naming-convention.md).
Multiple EF majors install side-by-side because `[NuGetPackage]` puts each
in its own tool-path cache:

```csharp
[NuGetPackage("dotnet-ef", Version = "8.0.20")]   readonly Tool Ef8;
[NuGetPackage("dotnet-ef", Version = "10.0.7")]   readonly Tool Ef10;
```

Requires `Tamp.Core ≥ 1.0.0`.

## Why a separate repo

EF Core ships patches every few weeks and minors that change CLI flags
(EF 8 → 9 → 10 in 2024–2025 alone). Coupling `tamp` core's release
cadence to that churn would either pin `tamp` behind EF or drag every
`tamp` patch through EF's release window. Per the satellite-repo
convention, each EF major tracks its own cadence here.

## Install

In your build script's `Directory.Packages.props`:

```xml
<PackageVersion Include="Tamp.EFCore.V10" Version="0.0.1-alpha" />
```

In `build/Build.csproj`:

```xml
<PackageReference Include="Tamp.EFCore.V10" />
```

## Quick example

```csharp
using Tamp;
using Tamp.EFCore.V10;
using Tamp.NetCli.V10;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Solution] readonly Solution Solution = null!;

    [NuGetPackage("dotnet-ef", Version = "10.0.7")]
    readonly Tool Ef = null!;

    AbsolutePath SrcProject => RootDirectory / "src" / "MyApp.Data" / "MyApp.Data.csproj";
    AbsolutePath StartupProject => RootDirectory / "src" / "MyApp.Api" / "MyApp.Api.csproj";

    // Until TAM-78 lands [Secret] env-var resolution in Tamp.Core 1.0.1,
    // load the connection string manually from env:
    static readonly Secret? Connection =
        Environment.GetEnvironmentVariable("EF_CONNECTION") is { Length: > 0 } v
            ? new Secret("EF connection string", v) : null;

    Target ApplyMigrations => _ => _
        .Requires(() => Connection != null)
        .Executes(() => EFCore.DatabaseUpdate(Ef, s => s
            .SetProject(SrcProject)
            .SetStartupProject(StartupProject)
            .SetConnection(Connection!)
            .SetNoBuild(true)));

    Target AddMigration => _ => _
        .Executes(() => EFCore.MigrationsAdd(Ef, s => s
            .SetName("AddCustomerEmail")
            .SetProject(SrcProject)
            .SetStartupProject(StartupProject)
            .SetOutputDir("Migrations")));

    Target BundleMigrations => _ => _
        .Executes(() => EFCore.MigrationsBundle(Ef, s => s
            .SetProject(SrcProject)
            .SetStartupProject(StartupProject)
            .SetOutput(Artifacts / "migrate")
            .SetTargetRuntime("linux-x64")
            .SetSelfContained(true)
            .SetForce(true)));
}
```

## Surface

Every `dotnet ef` verb is wrapped — see
[`src/Tamp.EFCore.V10/EFCore.cs`](src/Tamp.EFCore.V10/EFCore.cs) for the
flat facade:

- `database` — `Update`, `Drop`
- `dbcontext` — `Info`, `List`, `Optimize`, `Script`, `Scaffold`
- `migrations` — `Add`, `Remove`, `List`, `Script`, `Bundle`, `HasPendingModelChanges`

Every verb inherits the cross-project / cross-build flag set:
`--project`, `--startup-project`, `--framework`, `--configuration`,
`--runtime`, `--no-build`, `--msbuildprojectextensionspath`. Connection
strings on `database update`, `migrations list`, and `dbcontext scaffold`
are typed as `Secret` and registered with the runner's redaction table.

V8 lacks `--precompile-queries` and `--nativeaot` on `dbcontext optimize`
(those are EF 9+). All other surface is identical across V8 / V9 / V10.

## See also

- [tamp](https://github.com/tamp-build/tamp) — the core framework
- [Tamp ADR 0002](https://github.com/tamp-build/tamp/blob/main/docs/adr/0002-package-naming-convention.md) — package naming convention
- [TAM-78](https://github.com/tamp-build/tamp/issues) — `[Secret]` resolver patch (1.0.1)

## License

[MIT](LICENSE) — same as `tamp` core.
