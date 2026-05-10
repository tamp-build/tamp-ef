namespace Tamp.EFCore.V9;

/// <summary>
/// Wrapper for the <c>dotnet ef</c> CLI (Entity Framework Core 10.x).
/// Every verb in the CLI tree is surfaced as a static method that
/// builds a <see cref="CommandPlan"/> ready for the runner to execute.
/// </summary>
/// <remarks>
/// <para>
/// Resolve the tool itself via <c>[NuGetPackage]</c>. EF's CLI is
/// distributed as the <c>dotnet-ef</c> .NET tool:
/// </para>
/// <code>
/// [NuGetPackage("dotnet-ef", Version = "10.0.1")]
/// readonly Tool Ef;
/// </code>
/// <para>
/// Multiple EF majors install side-by-side because each version goes
/// into its own tool-path cache directory. So a build script can drive
/// EF 8, 9, and 10 projects in the same run by declaring three
/// <c>[NuGetPackage]</c> properties at different versions.
/// </para>
/// <para>
/// Connection strings are typed as <see cref="Secret"/> and are added
/// to the resulting <see cref="CommandPlan.Secrets"/> list so the
/// runner's redaction table covers them in any logged output.
/// </para>
/// </remarks>
public static class EFCore
{
    // ----- database -----

    /// <summary><c>dotnet ef database update</c> — apply pending migrations (or roll back to a target).</summary>
    public static CommandPlan DatabaseUpdate(Tool tool, Action<EFCoreDatabaseUpdateSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef database drop</c> — drop the database for the configured DbContext.</summary>
    public static CommandPlan DatabaseDrop(Tool tool, Action<EFCoreDatabaseDropSettings>? configure = null)
        => Build(tool, configure);

    // ----- dbcontext -----

    /// <summary><c>dotnet ef dbcontext info</c> — print metadata about the resolved DbContext.</summary>
    public static CommandPlan DbContextInfo(Tool tool, Action<EFCoreDbContextInfoSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef dbcontext list</c> — enumerate every DbContext in the project.</summary>
    public static CommandPlan DbContextList(Tool tool, Action<EFCoreDbContextListSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef dbcontext optimize</c> — generate compiled model and optionally precompiled queries / NativeAOT prep (EF 9+).</summary>
    public static CommandPlan DbContextOptimize(Tool tool, Action<EFCoreDbContextOptimizeSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef dbcontext script</c> — emit SQL to create the schema described by the current model.</summary>
    public static CommandPlan DbContextScript(Tool tool, Action<EFCoreDbContextScriptSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef dbcontext scaffold</c> — reverse-engineer a DbContext + entity classes from a database. Requires connection and provider.</summary>
    public static CommandPlan DbContextScaffold(Tool tool, Action<EFCoreDbContextScaffoldSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        return Build(tool, configure);
    }

    // ----- migrations -----

    /// <summary><c>dotnet ef migrations add &lt;name&gt;</c> — scaffold a new migration capturing model changes since the last one. Requires Name.</summary>
    public static CommandPlan MigrationsAdd(Tool tool, Action<EFCoreMigrationsAddSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        return Build(tool, configure);
    }

    /// <summary><c>dotnet ef migrations remove</c> — drop the most recent migration's files.</summary>
    public static CommandPlan MigrationsRemove(Tool tool, Action<EFCoreMigrationsRemoveSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef migrations list</c> — enumerate every migration the project knows about.</summary>
    public static CommandPlan MigrationsList(Tool tool, Action<EFCoreMigrationsListSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef migrations script [&lt;from&gt;] [&lt;to&gt;]</c> — emit SQL for a migration range.</summary>
    public static CommandPlan MigrationsScript(Tool tool, Action<EFCoreMigrationsScriptSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef migrations bundle</c> — package every migration into a self-contained executable.</summary>
    public static CommandPlan MigrationsBundle(Tool tool, Action<EFCoreMigrationsBundleSettings>? configure = null)
        => Build(tool, configure);

    /// <summary><c>dotnet ef migrations has-pending-model-changes</c> — exit non-zero when there are uncaptured model changes (EF 8+).</summary>
    public static CommandPlan MigrationsHasPendingModelChanges(Tool tool, Action<EFCoreMigrationsHasPendingModelChangesSettings>? configure = null)
        => Build(tool, configure);

    private static CommandPlan Build<T>(Tool tool, Action<T>? configure) where T : EFCoreSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var settings = new T();
        configure?.Invoke(settings);
        return settings.ToCommandPlan(tool);
    }
}
