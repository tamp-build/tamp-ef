namespace Tamp.EFCore.V10;

/// <summary>
/// Settings for <c>dotnet ef database update</c>: apply pending
/// migrations (or roll back to a named migration when one is supplied).
/// </summary>
public sealed class EFCoreDatabaseUpdateSettings : EFCoreSettingsBase
{
    /// <summary>Optional target migration. <c>null</c> means "apply all pending"; <c>"0"</c> rolls back every migration.</summary>
    public string? TargetMigration { get; set; }

    /// <summary>Override the connection string. Maps to <c>--connection</c>. Pass as <see cref="Secret"/> so it's redacted in logs.</summary>
    public Secret? Connection { get; set; }

    /// <summary>The DbContext class name when the project has more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    /// <summary>Print SQL the migration would run without executing. Maps to <c>--dry-run</c> (EF Core 9+).</summary>
    public bool DryRun { get; set; }

    public EFCoreDatabaseUpdateSettings SetTargetMigration(string? migration) { TargetMigration = migration; return this; }
    public EFCoreDatabaseUpdateSettings SetConnection(Secret connection) { Connection = connection; return this; }
    public EFCoreDatabaseUpdateSettings SetContext(string? context) { Context = context; return this; }
    public EFCoreDatabaseUpdateSettings SetDryRun(bool v = true) { DryRun = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "database";
        yield return "update";
        if (!string.IsNullOrEmpty(TargetMigration)) yield return TargetMigration!;
        if (Connection is { } c) { yield return "--connection"; yield return c.Reveal(); }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
        if (DryRun) yield return "--dry-run";
    }

    protected override IReadOnlyList<Secret> BuildSecrets()
        => Connection is null ? Array.Empty<Secret>() : new[] { Connection };
}

/// <summary>
/// Settings for <c>dotnet ef database drop</c>: drop the database for
/// the configured DbContext.
/// </summary>
public sealed class EFCoreDatabaseDropSettings : EFCoreSettingsBase
{
    /// <summary>Skip the confirmation prompt. Maps to <c>--force</c>. CI default.</summary>
    public bool Force { get; set; }

    /// <summary>Print what would happen without executing. Maps to <c>--dry-run</c>.</summary>
    public bool DryRun { get; set; }

    /// <summary>Override the connection string. Maps to <c>--connection</c>.</summary>
    public Secret? Connection { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreDatabaseDropSettings SetForce(bool v = true) { Force = v; return this; }
    public EFCoreDatabaseDropSettings SetDryRun(bool v = true) { DryRun = v; return this; }
    public EFCoreDatabaseDropSettings SetConnection(Secret connection) { Connection = connection; return this; }
    public EFCoreDatabaseDropSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "database";
        yield return "drop";
        if (Force) yield return "--force";
        if (DryRun) yield return "--dry-run";
        if (Connection is { } c) { yield return "--connection"; yield return c.Reveal(); }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }

    protected override IReadOnlyList<Secret> BuildSecrets()
        => Connection is null ? Array.Empty<Secret>() : new[] { Connection };
}
