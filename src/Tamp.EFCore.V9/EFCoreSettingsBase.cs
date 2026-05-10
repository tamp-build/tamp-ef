namespace Tamp.EFCore.V9;

/// <summary>
/// Verbosity / logging knobs accepted by every <c>dotnet ef</c> verb.
/// </summary>
public enum EFCoreVerbosity
{
    /// <summary>Default — emit only what the verb normally prints.</summary>
    Default,
    /// <summary>Add <c>--verbose</c> to the command.</summary>
    Verbose,
}

/// <summary>
/// Common base for every <c>dotnet ef</c> verb's settings. Holds the
/// cross-project / cross-build flags that the EF CLI exposes on (almost)
/// every subcommand: <c>--project</c>, <c>--startup-project</c>,
/// <c>--framework</c>, <c>--configuration</c>, <c>--runtime</c>,
/// <c>--no-build</c>, <c>--msbuildprojectextensionspath</c>,
/// <c>--working-dir</c>, plus the global output-shaping flags
/// <c>--verbose</c>, <c>--no-color</c>, <c>--prefix-output</c>, and
/// <c>--json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>dotnet ef</c> invocation runs out of <see cref="Project"/>
/// (the project that contains your <c>DbContext</c> and migration
/// classes) but builds and loads <see cref="StartupProject"/> (the
/// runnable project whose DI / config the CLI uses to instantiate the
/// context). For single-project apps both flags can be left null.
/// </para>
/// <para>
/// <see cref="MsBuildProjectExtensionsPath"/> is the override for the
/// <c>obj/</c> location and is needed when your build pipeline
/// redirects MSBuild output (some monorepos do).
/// </para>
/// </remarks>
public abstract class EFCoreSettingsBase
{
    /// <summary>Path to the project containing the DbContext (the "migrations" project). Maps to <c>--project</c>.</summary>
    public string? Project { get; set; }

    /// <summary>Path to the runnable startup project the CLI bootstraps. Maps to <c>--startup-project</c>.</summary>
    public string? StartupProject { get; set; }

    /// <summary>Target framework when the project multi-targets. Maps to <c>--framework</c>.</summary>
    public string? Framework { get; set; }

    /// <summary>Build configuration (<c>Debug</c> / <c>Release</c>). Maps to <c>--configuration</c>.</summary>
    public string? Configuration { get; set; }

    /// <summary>RID for the build. Maps to <c>--runtime</c>.</summary>
    public string? Runtime { get; set; }

    /// <summary>Skip the build step before running the verb. Maps to <c>--no-build</c>.</summary>
    public bool NoBuild { get; set; }

    /// <summary>Override the MSBuild <c>obj/</c> directory. Maps to <c>--msbuildprojectextensionspath</c> (marked Obsolete by EF, but still accepted).</summary>
    public string? MsBuildProjectExtensionsPath { get; set; }

    /// <summary>Verbose output. Maps to <c>--verbose</c>.</summary>
    public EFCoreVerbosity Verbosity { get; set; }

    /// <summary>Suppress color in CLI output. Maps to <c>--no-color</c>.</summary>
    public bool NoColor { get; set; }

    /// <summary>Prefix output lines with the stream they came from. Maps to <c>--prefix-output</c>.</summary>
    public bool PrefixOutput { get; set; }

    /// <summary>Working directory of the spawned <c>dotnet</c> process. (EF's CLI has no <c>--working-dir</c> flag — set the OS process's CWD instead.)</summary>
    public string? ProcessWorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables on top of the inherited environment.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Subclasses build the per-verb argument list here, in order, starting with the verb words (e.g. <c>migrations</c> <c>add</c>).</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    /// <summary>Subclasses override when the plan should declare typed Secrets so the runner's redaction table covers their values.</summary>
    protected virtual IReadOnlyList<Secret> BuildSecrets() => Array.Empty<Secret>();

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        var args = new List<string>(BuildVerbArguments());
        AddCommonArguments(args);
        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = ProcessWorkingDirectory ?? tool.WorkingDirectory,
            Secrets = BuildSecrets(),
        };
    }

    private void AddCommonArguments(List<string> args)
    {
        if (!string.IsNullOrEmpty(Project)) { args.Add("--project"); args.Add(Project!); }
        if (!string.IsNullOrEmpty(StartupProject)) { args.Add("--startup-project"); args.Add(StartupProject!); }
        if (!string.IsNullOrEmpty(Framework)) { args.Add("--framework"); args.Add(Framework!); }
        if (!string.IsNullOrEmpty(Configuration)) { args.Add("--configuration"); args.Add(Configuration!); }
        if (!string.IsNullOrEmpty(Runtime)) { args.Add("--runtime"); args.Add(Runtime!); }
        if (NoBuild) args.Add("--no-build");
        if (!string.IsNullOrEmpty(MsBuildProjectExtensionsPath)) { args.Add("--msbuildprojectextensionspath"); args.Add(MsBuildProjectExtensionsPath!); }
        if (Verbosity == EFCoreVerbosity.Verbose) args.Add("--verbose");
        if (NoColor) args.Add("--no-color");
        if (PrefixOutput) args.Add("--prefix-output");
    }
}

/// <summary>
/// Generic fluent helpers for the common knobs. Each settings subclass
/// inherits these via its own typed setter overrides (so the chain
/// stays in the subclass type for IntelliSense).
/// </summary>
public static class EFCoreSettingsBaseExtensions
{
    public static T SetProject<T>(this T s, string? project) where T : EFCoreSettingsBase { s.Project = project; return s; }
    public static T SetStartupProject<T>(this T s, string? project) where T : EFCoreSettingsBase { s.StartupProject = project; return s; }
    public static T SetFramework<T>(this T s, string? framework) where T : EFCoreSettingsBase { s.Framework = framework; return s; }
    public static T SetConfiguration<T>(this T s, string? configuration) where T : EFCoreSettingsBase { s.Configuration = configuration; return s; }
    public static T SetRuntime<T>(this T s, string? runtime) where T : EFCoreSettingsBase { s.Runtime = runtime; return s; }
    public static T SetNoBuild<T>(this T s, bool v = true) where T : EFCoreSettingsBase { s.NoBuild = v; return s; }
    public static T SetMsBuildProjectExtensionsPath<T>(this T s, string? path) where T : EFCoreSettingsBase { s.MsBuildProjectExtensionsPath = path; return s; }
    public static T SetVerbose<T>(this T s, bool v = true) where T : EFCoreSettingsBase { s.Verbosity = v ? EFCoreVerbosity.Verbose : EFCoreVerbosity.Default; return s; }
    public static T SetNoColor<T>(this T s, bool v = true) where T : EFCoreSettingsBase { s.NoColor = v; return s; }
    public static T SetPrefixOutput<T>(this T s, bool v = true) where T : EFCoreSettingsBase { s.PrefixOutput = v; return s; }
    public static T SetProcessWorkingDirectory<T>(this T s, string? path) where T : EFCoreSettingsBase { s.ProcessWorkingDirectory = path; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : EFCoreSettingsBase { s.EnvironmentVariables[name] = value; return s; }
}
