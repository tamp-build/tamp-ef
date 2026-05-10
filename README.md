# Tamp.EF

`dotnet ef` wrappers for [Tamp](https://github.com/tamp-build/tamp).

Three sibling packages, each pinned to an EF Core major:

| Package | EF Core | .NET runtime | Status |
|---|---|---|---|
| [`Tamp.EFCore.V8`](src/Tamp.EFCore.V8) | 8.x (LTS through Nov 2026) | net8.0 / net9.0 / net10.0 | live |
| [`Tamp.EFCore.V9`](src/Tamp.EFCore.V9) | 9.x (STS through May 2026) | net8.0 / net9.0 / net10.0 | live |
| [`Tamp.EFCore.V10`](src/Tamp.EFCore.V10) | 10.x (LTS through Nov 2028) | net8.0 / net9.0 / net10.0 | live |

The package matches the EF Core major you need to drive — not the runtime
your build script targets. The CLI surface (and its flags) is what's
version-pinned, per [Tamp ADR 0002](https://github.com/tamp-build/tamp/blob/main/docs/adr/0002-package-naming-convention.md).
Multiple versions install side-by-side because `[NuGetPackage]` puts each in
its own tool-path cache.

## Why a separate repo

EF Core ships patches every few weeks (and minors that change CLI flags).
Coupling `tamp` core's release cadence to that churn would either pin
`tamp` behind EF or drag every `tamp` patch through EF's release window.
Splitting `tamp-ef` out lets each major track its own cadence independently
of `tamp` core. See [the discussion in tamp #DESIGN.md](https://github.com/tamp-build/tamp/blob/main/DESIGN.md)
for the full reasoning.

## Quick example

```csharp
using Tamp;
using Tamp.EFCore.V10;
using Tamp.NetCli.V10;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Solution] readonly Solution Solution = null!;

    [NuGetPackage("dotnet-ef", Version = "10.0.1")]
    readonly Tool Ef = null!;

    [Parameter("Connection string for migrations")]
    [Secret("EF connection string", EnvironmentVariable = "EF_CONNECTION")]
    readonly Secret Connection = null!;

    AbsolutePath SrcProject => RootDirectory / "src" / "MyApp.Data" / "MyApp.Data.csproj";
    AbsolutePath StartupProject => RootDirectory / "src" / "MyApp.Api" / "MyApp.Api.csproj";

    Target ApplyMigrations => _ => _
        .Executes(() => EFCore.DatabaseUpdate(Ef, s => s
            .SetProject(SrcProject)
            .SetStartupProject(StartupProject)
            .SetConnection(Connection)
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
            .SetOutput(Artifacts / "migrate.exe")
            .SetTargetRuntime("linux-x64")
            .SetSelfContained(true)
            .SetForce(true)));
}
```

## Surface — every `dotnet ef` verb is wrapped

Top-level facade: `EFCore.<Verb>(tool, configure)` — see
[`src/Tamp.EFCore.V10/EFCore.cs`](src/Tamp.EFCore.V10/EFCore.cs) for the
full list. Every verb's full command-line surface is exposed, including
the cross-project flags (`--project`, `--startup-project`, `--framework`,
`--configuration`, `--runtime`, `--no-build`, `--msbuildprojectextensionspath`,
`--working-dir`).

Connection strings are first-class `Secret` values — registered with the
runner's redaction table so they're scrubbed from logs.

## License

[MIT](LICENSE) — same as `tamp` core.
