namespace Tamp.EFCore.V9;

/// <summary>
/// Settings for <c>dotnet ef dbcontext info</c>: print metadata about
/// the resolved <see cref="Context"/>.
/// </summary>
public sealed class EFCoreDbContextInfoSettings : EFCoreSettingsBase
{
    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    /// <summary>Emit machine-readable JSON output. Maps to <c>--json</c>.</summary>
    public bool Json { get; set; }

    public EFCoreDbContextInfoSettings SetContext(string? context) { Context = context; return this; }
    public EFCoreDbContextInfoSettings SetJson(bool v = true) { Json = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "dbcontext";
        yield return "info";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
        if (Json) yield return "--json";
    }
}

/// <summary>
/// Settings for <c>dotnet ef dbcontext list</c>: enumerate every
/// <c>DbContext</c> the project / startup-project pair exposes.
/// </summary>
public sealed class EFCoreDbContextListSettings : EFCoreSettingsBase
{
    /// <summary>Emit machine-readable JSON output. Maps to <c>--json</c>.</summary>
    public bool Json { get; set; }

    public EFCoreDbContextListSettings SetJson(bool v = true) { Json = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "dbcontext";
        yield return "list";
        if (Json) yield return "--json";
    }
}

/// <summary>
/// Settings for <c>dotnet ef dbcontext optimize</c>: generate a
/// compiled model + (EF 9+) optionally precompiled queries / NativeAOT
/// preparation for fast startup.
/// </summary>
public sealed class EFCoreDbContextOptimizeSettings : EFCoreSettingsBase
{
    /// <summary>Output directory for the generated compiled-model files. Maps to <c>--output-dir</c>.</summary>
    public string? OutputDir { get; set; }

    /// <summary>Namespace for the generated compiled-model classes. Maps to <c>--namespace</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    /// <summary>Suffix appended to generated file names. Maps to <c>--suffix</c>.</summary>
    public string? Suffix { get; set; }

    /// <summary>Skip generating the compiled model — useful when you want only precompiled queries. Maps to <c>--no-scaffold</c>.</summary>
    public bool NoScaffold { get; set; }

    /// <summary>Generate precompiled queries alongside the compiled model. Maps to <c>--precompile-queries</c> (EF Core 9+).</summary>
    public bool PrecompileQueries { get; set; }

    /// <summary>Emit code that's ready for NativeAOT consumption. Maps to <c>--nativeaot</c> (EF Core 9+).</summary>
    public bool NativeAot { get; set; }

    public EFCoreDbContextOptimizeSettings SetOutputDir(string? dir) { OutputDir = dir; return this; }
    public EFCoreDbContextOptimizeSettings SetNamespace(string? ns) { Namespace = ns; return this; }
    public EFCoreDbContextOptimizeSettings SetContext(string? context) { Context = context; return this; }
    public EFCoreDbContextOptimizeSettings SetSuffix(string? suffix) { Suffix = suffix; return this; }
    public EFCoreDbContextOptimizeSettings SetNoScaffold(bool v = true) { NoScaffold = v; return this; }
    public EFCoreDbContextOptimizeSettings SetPrecompileQueries(bool v = true) { PrecompileQueries = v; return this; }
    public EFCoreDbContextOptimizeSettings SetNativeAot(bool v = true) { NativeAot = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "dbcontext";
        yield return "optimize";
        if (!string.IsNullOrEmpty(OutputDir)) { yield return "--output-dir"; yield return OutputDir!; }
        if (!string.IsNullOrEmpty(Namespace)) { yield return "--namespace"; yield return Namespace!; }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
        if (!string.IsNullOrEmpty(Suffix)) { yield return "--suffix"; yield return Suffix!; }
        if (NoScaffold) yield return "--no-scaffold";
        if (PrecompileQueries) yield return "--precompile-queries";
        if (NativeAot) yield return "--nativeaot";
    }
}

/// <summary>
/// Settings for <c>dotnet ef dbcontext script</c>: emit the SQL
/// that would create the schema described by the current model
/// (CREATE TABLE … from the snapshot, no migration history involved).
/// </summary>
public sealed class EFCoreDbContextScriptSettings : EFCoreSettingsBase
{
    /// <summary>Output file (defaults to stdout). Maps to <c>--output</c>.</summary>
    public string? Output { get; set; }

    /// <summary>The DbContext class name when there's more than one. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    public EFCoreDbContextScriptSettings SetOutput(string? path) { Output = path; return this; }
    public EFCoreDbContextScriptSettings SetContext(string? context) { Context = context; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "dbcontext";
        yield return "script";
        if (!string.IsNullOrEmpty(Output)) { yield return "--output"; yield return Output!; }
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
    }
}

/// <summary>
/// Settings for <c>dotnet ef dbcontext scaffold &lt;connection&gt; &lt;provider&gt;</c>:
/// reverse-engineer a DbContext + entity classes from a database.
/// </summary>
public sealed class EFCoreDbContextScaffoldSettings : EFCoreSettingsBase
{
    /// <summary>Database connection string. Required. Pass as <see cref="Secret"/> so it's redacted.</summary>
    public Secret? Connection { get; set; }

    /// <summary>EF provider package (e.g. <c>Microsoft.EntityFrameworkCore.SqlServer</c>). Required.</summary>
    public string? Provider { get; set; }

    /// <summary>Use data annotations (<c>[Required]</c> etc.) instead of fluent API. Maps to <c>--data-annotations</c>.</summary>
    public bool DataAnnotations { get; set; }

    /// <summary>Class name for the generated DbContext. Maps to <c>--context</c>.</summary>
    public string? Context { get; set; }

    /// <summary>Directory the DbContext is generated into. Maps to <c>--context-dir</c>.</summary>
    public string? ContextDir { get; set; }

    /// <summary>Namespace for the generated DbContext. Maps to <c>--context-namespace</c>.</summary>
    public string? ContextNamespace { get; set; }

    /// <summary>Overwrite existing files. Maps to <c>--force</c>.</summary>
    public bool Force { get; set; }

    /// <summary>Skip the generated <c>OnConfiguring</c> override. Maps to <c>--no-onconfiguring</c>.</summary>
    public bool NoOnConfiguring { get; set; }

    /// <summary>Don't pluralize entity-set names. Maps to <c>--no-pluralize</c>.</summary>
    public bool NoPluralize { get; set; }

    /// <summary>Output directory for entity classes. Maps to <c>--output-dir</c>.</summary>
    public string? OutputDir { get; set; }

    /// <summary>Namespace for entity classes. Maps to <c>--namespace</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>Use the database's identifier casing instead of C#-style. Maps to <c>--use-database-names</c>.</summary>
    public bool UseDatabaseNames { get; set; }

    /// <summary>Schemas to include. Repeated as <c>--schema &lt;name&gt;</c>.</summary>
    public List<string> Schemas { get; } = [];

    /// <summary>Tables to include. Repeated as <c>--table &lt;name&gt;</c>. Supports schema-qualified names (e.g. <c>dbo.Users</c>).</summary>
    public List<string> Tables { get; } = [];

    /// <summary>Emit machine-readable JSON output. Maps to <c>--json</c>.</summary>
    public bool Json { get; set; }

    public EFCoreDbContextScaffoldSettings SetConnection(Secret connection) { Connection = connection; return this; }
    public EFCoreDbContextScaffoldSettings SetProvider(string? provider) { Provider = provider; return this; }
    public EFCoreDbContextScaffoldSettings SetDataAnnotations(bool v = true) { DataAnnotations = v; return this; }
    public EFCoreDbContextScaffoldSettings SetContext(string? context) { Context = context; return this; }
    public EFCoreDbContextScaffoldSettings SetContextDir(string? dir) { ContextDir = dir; return this; }
    public EFCoreDbContextScaffoldSettings SetContextNamespace(string? ns) { ContextNamespace = ns; return this; }
    public EFCoreDbContextScaffoldSettings SetForce(bool v = true) { Force = v; return this; }
    public EFCoreDbContextScaffoldSettings SetNoOnConfiguring(bool v = true) { NoOnConfiguring = v; return this; }
    public EFCoreDbContextScaffoldSettings SetNoPluralize(bool v = true) { NoPluralize = v; return this; }
    public EFCoreDbContextScaffoldSettings SetOutputDir(string? dir) { OutputDir = dir; return this; }
    public EFCoreDbContextScaffoldSettings SetNamespace(string? ns) { Namespace = ns; return this; }
    public EFCoreDbContextScaffoldSettings SetUseDatabaseNames(bool v = true) { UseDatabaseNames = v; return this; }
    public EFCoreDbContextScaffoldSettings AddSchema(string schema) { Schemas.Add(schema); return this; }
    public EFCoreDbContextScaffoldSettings AddTable(string table) { Tables.Add(table); return this; }
    public EFCoreDbContextScaffoldSettings SetJson(bool v = true) { Json = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (Connection is null) throw new InvalidOperationException("Connection is required for scaffold (set via SetConnection).");
        if (string.IsNullOrEmpty(Provider)) throw new InvalidOperationException("Provider is required for scaffold (set via SetProvider).");

        yield return "dbcontext";
        yield return "scaffold";
        yield return Connection.Reveal();
        yield return Provider!;

        if (DataAnnotations) yield return "--data-annotations";
        if (!string.IsNullOrEmpty(Context)) { yield return "--context"; yield return Context!; }
        if (!string.IsNullOrEmpty(ContextDir)) { yield return "--context-dir"; yield return ContextDir!; }
        if (!string.IsNullOrEmpty(ContextNamespace)) { yield return "--context-namespace"; yield return ContextNamespace!; }
        if (Force) yield return "--force";
        if (NoOnConfiguring) yield return "--no-onconfiguring";
        if (NoPluralize) yield return "--no-pluralize";
        if (!string.IsNullOrEmpty(OutputDir)) { yield return "--output-dir"; yield return OutputDir!; }
        if (!string.IsNullOrEmpty(Namespace)) { yield return "--namespace"; yield return Namespace!; }
        if (UseDatabaseNames) yield return "--use-database-names";
        foreach (var s in Schemas) { yield return "--schema"; yield return s; }
        foreach (var t in Tables) { yield return "--table"; yield return t; }
        if (Json) yield return "--json";
    }

    protected override IReadOnlyList<Secret> BuildSecrets()
        => Connection is null ? Array.Empty<Secret>() : new[] { Connection };
}
