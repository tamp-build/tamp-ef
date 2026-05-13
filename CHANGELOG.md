# Changelog

All notable changes to Tamp.EFCore (V8 / V9 / V10) are documented in this file.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and the project follows [Semantic Versioning 2.0.0](https://semver.org/). Pre-1.0 versions may break public API freely between minor versions; the `0.x` line is intentionally a stabilization run.

## [Unreleased]

## [0.3.1] — 2026-05-13

### Added (V10 only)

- **`MigrationTarget.FromConnectionString(Secret connectionString, string? name, string? environment, string? tier)`** overload (TAM-183). The existing string-typed overload still works; this Secret-typed sibling preserves redaction through the build pipeline for adopters carrying tenant connection strings as `Secret`. When no explicit `name` is supplied, the Secret's existing `Name` carries through to the resulting `MigrationTarget.Name` (handy when adopters key their secrets like `TENANT_3__CONNECTION` and want the same identifier in fan-out reports).

  ```csharp
  IReadOnlyList<MigrationTarget> tenants = settings
      .Where(kvp => kvp.Key.StartsWith("TENANT_"))
      .Select(kvp => MigrationTarget.FromConnectionString(
          new Secret(kvp.Key, kvp.Value)))
      .ToList();
  ```

### Notes

- Driven by strata-scott's 2026-05-13 universal-friction report after wiring `ApplyTenantMigrations` in Strata's `build/Build.cs`. Multi-tenant adopters who carry connection strings as `Secret` (the canonical shape for credentialed material) previously had no public path to convert into a `MigrationTarget` without round-tripping through a plain string and losing the redaction wrap.

## [0.3.0] — 2026-05-12

### Changed (breaking, V10 only)

- **`EFCore.MigrationsBundle(...)` now returns `MigrationBundlePlan` instead of `CommandPlan`** (TAM-165). The new type is implicitly convertible to `CommandPlan`, so existing target bodies that return `EFCore.MigrationsBundle(...)` directly keep compiling. Tests that accessed `.Arguments` / `.Executable` on the return value need to go through `.Plan.Arguments` / `.Plan.Executable`.

### Added (V10 only)

- **Fluent per-tenant SaaS migration loop** (TAM-104 / TAM-165). Build the bundle and fan it out across N tenants in one call. strata-scott's design lands verbatim modulo the framework's `Tool`-as-first-arg convention:

  ```csharp
  Target ApplyTenantMigrations => _ => _.Executes(async () =>
  {
      // How you build the tenant catalog is project-specific — Tamp doesn't opine.
      IReadOnlyList<MigrationTarget> tenants = await MyTenantCatalog.LoadAsync();

      await EFCore.MigrationsBundle(Ef, s => s.SetProject(Solution.Path).SetSelfContained())
          .ForEachTenantAsync(
              tenants,
              parallelism: 4,
              onFailure: TenantFailureMode.LogAndContinue,
              timeoutPerTenant: TimeSpan.FromMinutes(5));
  });
  ```

- **`TenantFailureMode`** enum, distinct from the engine-level `FanoutMode`:
  - `FailFast` — stop on first failure, skip the rest, throw aggregate at end
  - `LogAndContinue` (default) — run every tenant, log each failure, throw aggregate at end so CI sees a non-zero exit
  - `ReportOnly` — run every tenant, never throw — caller inspects `MigrationFanoutResult` and decides

- **`MigrationTarget.FromConnectionString(connStr, name?, environment?, tier?)`** convenience static. Saves the three-line `new MigrationTarget(..., new Secret(...), ...)` wrap when the adopter already has a plain connection string in hand.

- **`MigrationBundlePlan.BuildAsync(ct?)`** — explicit "build the bundle, return the path" entry point for callers who want to produce the bundle as a deployment artifact separately from running it. The fluent `ForEachTenantAsync` calls this internally.

### Notes

- V8 and V9 do not have the fan-out feature yet — `MigrationsBundle` on those still returns `CommandPlan`. Strata's stack is on .NET 10, so the V10 surface is the focus. Backporting to V8/V9 will land in a future wave if any adopter asks.
- Conceded to strata-scott's design 2026-05-12 after he flagged the prior split `EFCoreMigrationFanout.RunAsync(bundlePath, ...)` API as too low-level for the common case. The split engine API stays public for power users who want bundle-once-fanout-many.
- `TenantCatalog.FromMasterDb(...)` deliberately does NOT exist. How the adopter derives their tenant list (master-DB query, KV lookup, hard-coded list) is project-specific — Tamp consumes an `IReadOnlyList<MigrationTarget>` and does not opine on its provenance.

## [0.2.1] — 2026-05-11

### Added

- Object-init overloads on every EF wrapper (TAM-161 satellite fanout). Each public `EFCore.Verb(Tool tool, Action<TSettings>? configure = null)` method now has a parallel `EFCore.Verb(Tool tool, TSettings settings)` overload. Both authoring styles produce byte-equal `CommandPlan`s; the fluent form remains canonical in docs.

  ```csharp
  // Fluent (canonical):
  EFCore.MigrationsAdd(Ef, s => s.SetName("InitialCreate").SetProject(Solution.Path));

  // Object-init (alternative):
  EFCore.MigrationsAdd(Ef, new() { Name = "InitialCreate", Project = Solution.Path });
  ```

  Overloads added on all three EF majors: `DatabaseUpdate`, `DatabaseDrop`, `DbContextInfo`, `DbContextList`, `DbContextOptimize`, `DbContextScript`, `DbContextScaffold`, `MigrationsAdd`, `MigrationsRemove`, `MigrationsList`, `MigrationsScript`, `MigrationsBundle`, `MigrationsHasPendingModelChanges`.
