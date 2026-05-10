namespace Tamp.EFCore.V10;

/// <summary>
/// Settings for <c>dotnet ef migrations add &lt;name&gt;</c>: scaffold a
/// new migration that captures the model delta since the last migration.
/// </summary>
public sealed class EFCoreMigrationsAddSettings : EFCoreSettingsBase
{
    /// <summary>Migration name (PascalCase, no spaces). Required.</summary>
    public string? Name { get; set; }

    /// <summary>Output directory for the new migration files. Maps to <c>--output-dir</c>.</summary>
    public string? OutputDir { get; set; }

    /// <summary>Namespace for the generated migration class. Maps to <c>--namespace</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsAddSettings SetName(string? name) { Name = name; return this; }
    public EFCoreMigrationsAddSettings SetOutputDir(string? dir) { OutputDir = dir; return this; }
    public EFCoreMigrationsAddSettings SetNamespace(string? ns) { Namespace = ns; return this; }
    public EFCoreMigrationsAddSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(Name)) throw new InvalidOperationException("Name is required for migrations add (set via SetName).");

        yield return "migrations";
        yield return "add";
        yield return Name!;
        if (!string.IsNullOrEmpty(OutputDir)) { yield return "--output-dir"; yield return OutputDir!; }
        if (!string.IsNullOrEmpty(Namespace)) { yield return "--namespace"; yield return Namespace!; }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}

/// <summary>
/// Settings for <c>dotnet ef migrations remove</c>: drop the most
/// recent migration's files (and optionally revert from the database
/// when it's already applied).
/// </summary>
public sealed class EFCoreMigrationsRemoveSettings : EFCoreSettingsBase
{
    /// <summary>Remove the migration even if it's already applied to the database. Maps to <c>--force</c>.</summary>
    public bool Force { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsRemoveSettings SetForce(bool v = true) { Force = v; return this; }
    public EFCoreMigrationsRemoveSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "migrations";
        yield return "remove";
        if (Force) yield return "--force";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}

/// <summary>
/// Settings for <c>dotnet ef migrations list</c>: enumerate every
/// migration the project knows about, optionally annotating which are
/// applied to the connected database.
/// </summary>
public sealed class EFCoreMigrationsListSettings : EFCoreSettingsBase
{
    /// <summary>Override the connection string used to look up applied migrations. Maps to <c>--connection</c>.</summary>
    public Secret? Connection { get; set; }

    /// <summary>Don't connect to the database; just list what's in the project. Maps to <c>--no-connect</c>.</summary>
    public bool NoConnect { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsListSettings SetConnection(Secret connection) { Connection = connection; return this; }
    public EFCoreMigrationsListSettings SetNoConnect(bool v = true) { NoConnect = v; return this; }
    public EFCoreMigrationsListSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "migrations";
        yield return "list";
        if (Connection is { } c) { yield return "--connection"; yield return c.Reveal(); }
        if (NoConnect) yield return "--no-connect";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }

    protected override IReadOnlyList<Secret> BuildSecrets()
        => Connection is null ? Array.Empty<Secret>() : new[] { Connection };
}

/// <summary>
/// Settings for <c>dotnet ef migrations script [&lt;from&gt;] [&lt;to&gt;]</c>:
/// emit the SQL for a range of migrations.
/// </summary>
public sealed class EFCoreMigrationsScriptSettings : EFCoreSettingsBase
{
    /// <summary>Starting migration (exclusive). <c>null</c> means "the empty database". <c>"0"</c> is the canonical empty start.</summary>
    public string? From { get; set; }

    /// <summary>Ending migration (inclusive). <c>null</c> means "the latest".</summary>
    public string? To { get; set; }

    /// <summary>Output file (defaults to stdout). Maps to <c>--output</c>.</summary>
    public string? Output { get; set; }

    /// <summary>Generate a script safe to run repeatedly (checks the migrations history table). Maps to <c>--idempotent</c>.</summary>
    public bool Idempotent { get; set; }

    /// <summary>Skip wrapping each migration in a transaction. Maps to <c>--no-transactions</c>.</summary>
    public bool NoTransactions { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsScriptSettings SetFrom(string? from) { From = from; return this; }
    public EFCoreMigrationsScriptSettings SetTo(string? to) { To = to; return this; }
    public EFCoreMigrationsScriptSettings SetOutput(string? path) { Output = path; return this; }
    public EFCoreMigrationsScriptSettings SetIdempotent(bool v = true) { Idempotent = v; return this; }
    public EFCoreMigrationsScriptSettings SetNoTransactions(bool v = true) { NoTransactions = v; return this; }
    public EFCoreMigrationsScriptSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "migrations";
        yield return "script";
        // From / To are positional. EF accepts: script, script <from>, script <from> <to>.
        // Supplying To without From is invalid — flag it early rather than producing
        // a malformed command.
        if (string.IsNullOrEmpty(From) && !string.IsNullOrEmpty(To))
            throw new InvalidOperationException("migrations script: cannot specify To without From (positional CLI shape).");
        if (!string.IsNullOrEmpty(From)) yield return From!;
        if (!string.IsNullOrEmpty(To)) yield return To!;

        if (!string.IsNullOrEmpty(Output)) { yield return "--output"; yield return Output!; }
        if (Idempotent) yield return "--idempotent";
        if (NoTransactions) yield return "--no-transactions";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}

/// <summary>
/// Settings for <c>dotnet ef migrations bundle</c>: package every
/// migration into a self-contained executable that applies them at run
/// time. Useful for production deploys without the full SDK on the
/// target host.
/// </summary>
public sealed class EFCoreMigrationsBundleSettings : EFCoreSettingsBase
{
    /// <summary>Output executable path. Maps to <c>--output</c>.</summary>
    public string? Output { get; set; }

    /// <summary>Target RID for the bundle (e.g. <c>linux-x64</c>, <c>win-x64</c>). Maps to <c>--target-runtime</c>.</summary>
    public string? TargetRuntime { get; set; }

    /// <summary>Self-contained executable (bundles the .NET runtime). Maps to <c>--self-contained</c>.</summary>
    public bool SelfContained { get; set; }

    /// <summary>Overwrite an existing bundle. Maps to <c>--force</c>.</summary>
    public bool Force { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsBundleSettings SetOutput(string? path) { Output = path; return this; }
    public EFCoreMigrationsBundleSettings SetTargetRuntime(string? rid) { TargetRuntime = rid; return this; }
    public EFCoreMigrationsBundleSettings SetSelfContained(bool v = true) { SelfContained = v; return this; }
    public EFCoreMigrationsBundleSettings SetForce(bool v = true) { Force = v; return this; }
    public EFCoreMigrationsBundleSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "migrations";
        yield return "bundle";
        if (!string.IsNullOrEmpty(Output)) { yield return "--output"; yield return Output!; }
        if (!string.IsNullOrEmpty(TargetRuntime)) { yield return "--target-runtime"; yield return TargetRuntime!; }
        if (SelfContained) yield return "--self-contained";
        if (Force) yield return "--force";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}

/// <summary>
/// Settings for <c>dotnet ef migrations has-pending-model-changes</c>
/// (EF Core 8+): exit non-zero when the current model has changes that
/// no migration captures yet. Useful as a CI gate.
/// </summary>
public sealed class EFCoreMigrationsHasPendingModelChangesSettings : EFCoreSettingsBase
{
    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreMigrationsHasPendingModelChangesSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "migrations";
        yield return "has-pending-model-changes";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}
