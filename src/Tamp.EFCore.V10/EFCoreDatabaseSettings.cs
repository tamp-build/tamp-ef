namespace Tamp.EFCore.V10;

/// <summary>
/// Settings for <c>dotnet ef database update</c>: apply pending
/// migrations (or roll back to a named migration when one is supplied).
/// </summary>
/// <remarks>
/// Note: <c>database update</c> does not have a <c>--dry-run</c> flag.
/// Use <c>migrations script</c> to preview the SQL without applying it.
/// </remarks>
public sealed class EFCoreDatabaseUpdateSettings : EFCoreSettingsBase
{
    /// <summary>Optional target migration. <c>null</c> means "apply all pending"; <c>"0"</c> rolls back every migration.</summary>
    public string? TargetMigration { get; set; }

    /// <summary>Override the connection string. Maps to <c>--connection</c>. Pass as <see cref="Secret"/> so it's redacted in logs.</summary>
    public Secret? Connection { get; set; }

    /// <summary>The DbContext class name when the project has more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreDatabaseUpdateSettings SetTargetMigration(string? migration) { TargetMigration = migration; return this; }
    public EFCoreDatabaseUpdateSettings SetConnection(Secret connection) { Connection = connection; return this; }
    public EFCoreDatabaseUpdateSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "database";
        yield return "update";
        if (!string.IsNullOrEmpty(TargetMigration)) yield return TargetMigration!;
        if (Connection is { } c) { yield return "--connection"; yield return c.Reveal(); }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }

    protected override IReadOnlyList<Secret> BuildSecrets()
        => Connection is null ? Array.Empty<Secret>() : new[] { Connection };
}

/// <summary>
/// Settings for <c>dotnet ef database drop</c>: drop the database for
/// the configured DbContext.
/// </summary>
/// <remarks>
/// Note: <c>database drop</c> does not accept a <c>--connection</c>
/// override — the connection comes from the DbContext's
/// <c>OnConfiguring</c> / <c>AddDbContext</c> registration. Configure
/// the startup project so the right connection resolves.
/// </remarks>
public sealed class EFCoreDatabaseDropSettings : EFCoreSettingsBase
{
    /// <summary>Skip the confirmation prompt. Maps to <c>--force</c>. CI default.</summary>
    public bool Force { get; set; }

    /// <summary>Print what would happen without executing. Maps to <c>--dry-run</c>.</summary>
    public bool DryRun { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreDatabaseDropSettings SetForce(bool v = true) { Force = v; return this; }
    public EFCoreDatabaseDropSettings SetDryRun(bool v = true) { DryRun = v; return this; }
    public EFCoreDatabaseDropSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "database";
        yield return "drop";
        if (Force) yield return "--force";
        if (DryRun) yield return "--dry-run";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}
