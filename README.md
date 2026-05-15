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

## Per-tenant migration fan-out (V10, 0.2.0+)

> Status: V10-only in 0.2.0. V8/V9 will follow once the shape stabilises.

EF Core's `migrations bundle` produces a self-contained executable
that knows every migration up to your current model. Multi-tenant SaaS
shapes ("Postgres-per-tenant", "schema-per-tenant", "regional shard
fleet") need to run that ONE bundle against MANY connection strings.
This is not just `foreach (var t in tenants) RunBundle(t)` — the
real-world failure modes will eat you alive without a structured
runner.

```csharp
using Tamp;
using Tamp.NetCli.V10;
using Tamp.EFCore.V10;

[NuGetPackage("dotnet-ef", Version = "10.0.7")]
readonly Tool Ef = null!;

[Secret("Master DB", EnvironmentVariable = "MASTER_DB")]
readonly Secret MasterConn = null!;

AbsolutePath Bundle => Artifacts / (OperatingSystem.IsWindows() ? "migrations.exe" : "migrations");

Target BuildBundle => _ => _.Executes(() =>
    DotNet.Run(EFCore.MigrationsBundle(Ef, s => s
        .SetProject(RootDirectory / "src" / "Acme.Api")
        .SetOutput(Bundle)
        .SetSelfContained(true)
        .SetForce(true))));

Target ApplyMigrations => _ => _
    .DependsOn(nameof(BuildBundle))
    .Executes(async () =>
    {
        var targets = LoadTenantsFromControlPlane()
            .Select(t => new MigrationTarget(
                Name: t.Slug,
                ConnectionString: new Secret($"db-{t.Slug}", t.ConnectionString),
                Environment: "Production",
                Tier: t.Tier))   // "master" | "regional" | "tenant"
            .ToList();

        var result = await EFCoreMigrationFanout.RunAndThrowOnFailureAsync(
            Bundle, targets, o => o
                .SetConcurrency(4)
                .SetPerTargetTimeout(TimeSpan.FromMinutes(10))
                .SetMode(FanoutMode.ContinueOnError)
                .SetRetries(max: 2, delay: TimeSpan.FromSeconds(5))
                .SetShouldRetry((exit, stderr) =>
                    stderr.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("could not connect", StringComparison.OrdinalIgnoreCase))
                .SetProgressWriter(Console.Out));

        Console.WriteLine($"Done: {result.SucceededCount}/{result.PerTarget.Count} in {result.TotalDuration}");
    });
```

### Why this is harder than it looks

**1. Connection strings are secrets.** They contain passwords, SAS
keys, or signed tokens. The wrapper takes them as `Secret` and
registers each one with Tamp's process-spawn redaction so they don't
appear in CI logs. NEVER `Console.WriteLine` a connection string from
inside a custom retry policy; the wrapper redacts at the runner
boundary, not at every consumer site.

**2. Concurrency is a double-edged sword.** Setting `Concurrency = N`
means N parallel writers against potentially-shared infrastructure.
Common shapes and their concurrency answers:

| Shape | Safe concurrency | Why |
|---|---|---|
| One Postgres per tenant, dedicated instances | 8–32 | No shared lock domain. Limited by control-plane API rate. |
| One Postgres instance, schema-per-tenant | 1–4 | Migrations on shared catalog tables (e.g., `pg_class`) serialise. |
| Cosmos DB / DynamoDB per tenant | 16+ | No DDL locking. Limited by RU/s budget. |
| SQL Server elastic pool | 2–8 | DTU saturation, not lock contention, is the limit. |
| CockroachDB / Spanner | 1–2 | DDL is online but expensive; multiple parallel migrations cause range splits. |

Start at 1 and raise it only after you've measured. The fan-out's
default IS `Concurrency = 1` — explicitly safe.

**3. Failure does not mean "stop".** In a fleet of 200 tenants, one
DB being in a deletion grace period or holding a long-running query
can fail one migration. The default mode is `ContinueOnError` —
every tenant's outcome is captured in the `PerTarget` list. Use
`FailFast` only when migrations are inter-dependent (e.g., the master
must succeed before regional shards run).

**4. Retries must classify failures.** Transient failures (deadlocks,
connection drops, leader elections) want retry. Permanent failures
(bad credentials, unmigrated breaking schema, dropped tables) don't —
retrying just multiplies the alert noise. The wrapper takes a
`ShouldRetry` predicate that sees the exit code and stderr; pattern-
match on known transient signatures:

```csharp
.SetShouldRetry((exit, stderr) =>
    // Postgres
    stderr.Contains("deadlock detected") ||
    stderr.Contains("could not connect to server") ||
    stderr.Contains("the database system is starting up") ||
    // SQL Server
    stderr.Contains("Transport-level error") ||
    stderr.Contains("Cannot connect to") ||
    // Network
    exit == -1)  // wrapper-side timeout
```

Anything else stays as `Failed` after the first attempt, and the
runner moves on.

**5. The `__EFMigrationsHistory` table is your idempotency story.**
EF Core writes one row per applied migration. Re-running a bundle
against an already-migrated DB exits 0 immediately — that's the
idempotency contract. Don't add your own "is this tenant already
migrated?" pre-check; you'd be duplicating EF's logic and risking
drift. Just re-run the fan-out.

**6. Tier ordering matters.** Master schema, regional/shard schema,
and per-tenant schema typically have foreign references — master
must migrate first, then regional, then tenants. The wrapper does
NOT do this for you; partition your targets by `Tier` and run
multiple fan-outs sequentially:

```csharp
var byTier = targets.GroupBy(t => t.Tier).ToDictionary(g => g.Key, g => g.ToList());

await EFCoreMigrationFanout.RunAndThrowOnFailureAsync(
    Bundle, byTier["master"], o => o.SetConcurrency(1));
await EFCoreMigrationFanout.RunAndThrowOnFailureAsync(
    Bundle, byTier["regional"], o => o.SetConcurrency(4));
await EFCoreMigrationFanout.RunAndThrowOnFailureAsync(
    Bundle, byTier["tenant"], o => o.SetConcurrency(8));
```

**7. ASPNETCORE_ENVIRONMENT is compile-time-baked into the bundle.**
The bundle has its production connection string substitution logic
baked in at `dotnet ef migrations bundle` time. The `Environment`
property on `MigrationTarget` flows through to the bundle's env vars
at invocation, which lets the bundle's `IConfiguration` pick the
right `appsettings.{env}.json`. If your `Startup` reads connection
strings from config files keyed by environment, you MUST set this
correctly — otherwise the bundle ignores the `--connection` argument
in favour of whatever its config resolved.

**8. Cancellation returns the partial result.** A cancelled CT does
NOT throw `OperationCanceledException`; it returns the
`MigrationFanoutResult` populated with whatever finished plus
`Skipped` entries for targets that didn't. This is deliberate — the
SaaS observability story is "show me what completed and what
didn't," and `OperationCancelled` would hide that. If you want
fail-fast behavior, use `FanoutMode.FailFast`; if you want a hard
exception on cancel, check `result.SkippedCount > 0` and throw
yourself.

**9. Capture the result for observability.** Serialise
`MigrationFanoutResult` as JSON for log aggregation. Fields like
`Tier`, `Attempts`, and `Duration` answer "which tenant took
forever?" and "did anyone retry into success?" — exactly the
questions an on-call wants to ask at 3am.

### What `EFCoreMigrationFanout` does NOT do

- **Topology partitioning** — caller decides which tenants run together.
- **Compensation / rollback** — EF Core's `DatabaseUpdate(s.SetTargetMigration("Previous"))` is the rollback primitive. The fan-out can drive it (point `Bundle` at a downgrade bundle), but doesn't synthesise one.
- **Pre-flight schema diff** — use `EFCore.MigrationsScript(s.SetIdempotent(true))` to emit SQL for review. The fan-out applies; it doesn't preview.
- **DDL idempotency wrappers** — if your migration includes raw SQL with non-idempotent DDL, that's a migration-authoring concern.
- **Maintenance-window gating** — sleep / hold-until-window helpers belong in your build script, not the fan-out.

## See also

- [tamp](https://github.com/tamp-build/tamp) — the core framework
- [Tamp ADR 0002](https://github.com/tamp-build/tamp/blob/main/docs/adr/0002-package-naming-convention.md) — package naming convention
- [TAM-78](https://github.com/tamp-build/tamp/issues) — `[Secret]` resolver patch (1.0.1)

## Settings authoring style

Examples above use the fluent `Set*`-chain shape. Every wrapper verb also accepts a `new XxxSettings { ... }` object-init form — both produce identical `CommandPlan`s. The fluent shape stays canonical in docs and the `tamp init` template; opt into object-init scaffolding via `tamp init --settings-style=init`.

See [Build Script Authoring → Two authoring styles](https://github.com/tamp-build/tamp/wiki/Build-Script-Authoring#two-authoring-styles-for-wrapper-calls-120) on the wiki for the side-by-side comparison.

## License

[MIT](LICENSE) — same as `tamp` core.
