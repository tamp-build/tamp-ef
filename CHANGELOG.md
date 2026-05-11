# Changelog

All notable changes to Tamp.EFCore (V8 / V9 / V10) are documented in this file.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and the project follows [Semantic Versioning 2.0.0](https://semver.org/). Pre-1.0 versions may break public API freely between minor versions; the `0.x` line is intentionally a stabilization run.

## [Unreleased]

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
