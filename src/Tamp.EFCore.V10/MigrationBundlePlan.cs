using System.IO;
using Tamp;

namespace Tamp.EFCore.V10;

/// <summary>
/// How the fluent <see cref="MigrationBundlePlan.ForEachTenantAsync"/> surface treats per-tenant failures.
/// Maps onto the underlying <see cref="FanoutMode"/> plus a throw-at-end policy in the fluent layer.
/// </summary>
public enum TenantFailureMode
{
    /// <summary>
    /// Stop at the first failure; mark remaining tenants as <see cref="MigrationOutcome.Skipped"/>;
    /// throw <see cref="MigrationFanoutException"/> at the end. The fastest signal when one bad
    /// tenant means the rest are wasted work (shared schema invariants, for instance).
    /// </summary>
    FailFast,

    /// <summary>
    /// Run every tenant regardless of failures, log each as it lands, then throw
    /// <see cref="MigrationFanoutException"/> at the end if any failed or timed out.
    /// The SaaS-friendly default — you get the full report AND a non-zero exit on the
    /// CI step, so the dashboard sees the failure and the report tells you which tenants.
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Run every tenant regardless of failures, but never throw. The caller inspects
    /// the returned <see cref="MigrationFanoutResult"/> and decides what to do. Use
    /// when the fan-out is one stage of a larger orchestration and "some tenants
    /// failed" is a recoverable signal you handle in code, not by aborting the build.
    /// </summary>
    ReportOnly,
}

/// <summary>
/// Result of calling <see cref="EFCore.MigrationsBundle(Tool, System.Action{EFCoreMigrationsBundleSettings})"/>.
/// Holds the bundle-build settings + tool reference; can be implicitly used as a
/// <see cref="CommandPlan"/> (so a target body can return it directly), or it can be
/// awaited via <see cref="ForEachTenantAsync"/> to build the bundle and fan it out
/// across N tenants in one call.
/// </summary>
public sealed class MigrationBundlePlan
{
    private readonly Tool _tool;
    private readonly EFCoreMigrationsBundleSettings _settings;
    private readonly IBundleInvoker? _testBundleInvoker;

    internal MigrationBundlePlan(Tool tool, EFCoreMigrationsBundleSettings settings, IBundleInvoker? testBundleInvoker = null)
    {
        _tool = tool;
        _settings = settings;
        _testBundleInvoker = testBundleInvoker;
    }

    /// <summary>
    /// The underlying <see cref="CommandPlan"/> that invokes <c>dotnet ef migrations bundle</c>.
    /// Use this when you want the target body to execute the bundle build by itself
    /// (without chaining into a tenant fan-out) — for example, to produce the bundle
    /// as a deployment artifact.
    /// </summary>
    public CommandPlan Plan => _settings.ToCommandPlan(_tool);

    /// <summary>
    /// Implicit conversion lets a target body return the plan directly:
    /// <c>Target Bundle => _ => _.Executes(() => EFCore.MigrationsBundle(Ef, s => s.SetProject(...)));</c>
    /// </summary>
    public static implicit operator CommandPlan(MigrationBundlePlan plan) => plan.Plan;

    /// <summary>
    /// Build the bundle as configured. Returns the absolute path to the produced executable.
    /// If <see cref="EFCoreMigrationsBundleSettings.Output"/> isn't set, a temp path is
    /// generated and the settings are updated with it before the build runs.
    /// </summary>
    public Task<string> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Output))
        {
            var defaultPath = Path.Combine(Path.GetTempPath(), $"tamp-migrations-{Guid.NewGuid():N}.exe");
            _settings.SetOutput(defaultPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var exitCode = ProcessRunner.Execute(_settings.ToCommandPlan(_tool));
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"dotnet ef migrations bundle exited {exitCode}. The bundle was not produced at '{_settings.Output}'.");

        return Task.FromResult(_settings.Output!);
    }

    /// <summary>
    /// Build the migration bundle once, then invoke it against every tenant in <paramref name="tenants"/>.
    /// The canonical entry-point for per-tenant SaaS migration loops; the adopter supplies the tenant
    /// catalog (how you build that list is project-specific — master-DB query, KV lookup, hard-coded
    /// list for tests — Tamp doesn't opine).
    /// </summary>
    /// <param name="tenants">The tenant catalog. Each element is one migration target.</param>
    /// <param name="parallelism">How many tenants to migrate concurrently. Defaults to <c>1</c> (serial).
    /// 4-8 is typical for Postgres-per-tenant shapes with no shared tablespace contention. Set deliberately —
    /// large fan-outs against shared DB infrastructure can saturate write IO.</param>
    /// <param name="onFailure">How per-tenant failures are surfaced. Default <see cref="TenantFailureMode.LogAndContinue"/>.</param>
    /// <param name="timeoutPerTenant">Per-tenant wall-clock limit. Default 5 minutes. Tenants exceeding this
    /// are killed and marked <see cref="MigrationOutcome.TimedOut"/>.</param>
    /// <param name="maxRetries">How many times to re-attempt a failed tenant before giving up. 0 disables retries.</param>
    /// <param name="retryDelay">Backoff between retry attempts on the same tenant. Default 5 seconds.</param>
    /// <param name="shouldRetry">Predicate (exitCode, stderr) -> bool. Default retries everything up to <paramref name="maxRetries"/>.
    /// Set this to skip retries on permanent errors (bad password, missing extension, etc.).</param>
    /// <param name="progressWriter">If supplied, the fan-out writes one progress line per tenant completion.</param>
    /// <param name="verbose">Pass <c>--verbose</c> through to the bundle invocation.</param>
    /// <param name="cancellationToken">Cooperative cancellation; all running tenants are cancelled and the
    /// partial result is returned (rather than throwing) when this fires.</param>
    /// <returns>The aggregate result. Throws <see cref="MigrationFanoutException"/> if
    /// <paramref name="onFailure"/> is <see cref="TenantFailureMode.FailFast"/> or
    /// <see cref="TenantFailureMode.LogAndContinue"/> and any tenant failed.</returns>
    public async Task<MigrationFanoutResult> ForEachTenantAsync(
        IReadOnlyList<MigrationTarget> tenants,
        int parallelism = 1,
        TenantFailureMode onFailure = TenantFailureMode.LogAndContinue,
        TimeSpan? timeoutPerTenant = null,
        int maxRetries = 0,
        TimeSpan? retryDelay = null,
        Func<int, string, bool>? shouldRetry = null,
        TextWriter? progressWriter = null,
        bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        if (tenants is null) throw new ArgumentNullException(nameof(tenants));

        // Build the bundle exe (skipped via the test seam when _testBundleInvoker is supplied —
        // unit tests want to verify the parameter wiring without running dotnet ef).
        var bundlePath = _testBundleInvoker is null
            ? await BuildAsync(cancellationToken).ConfigureAwait(false)
            : _settings.Output ?? "test-bundle.exe";

        var engineMode = onFailure == TenantFailureMode.FailFast ? FanoutMode.FailFast : FanoutMode.ContinueOnError;

        void Configure(MigrationFanoutOptions o)
        {
            o.SetConcurrency(parallelism)
             .SetMode(engineMode)
             .SetPerTargetTimeout(timeoutPerTenant ?? TimeSpan.FromMinutes(5))
             .SetRetries(maxRetries, retryDelay)
             .SetProgressWriter(progressWriter)
             .SetVerbose(verbose);
            if (shouldRetry is not null) o.SetShouldRetry(shouldRetry);
        }

        var result = _testBundleInvoker is null
            ? await EFCoreMigrationFanout.RunAsync(bundlePath, tenants, Configure, cancellationToken).ConfigureAwait(false)
            : await EFCoreMigrationFanout.RunAsync(bundlePath, tenants, _testBundleInvoker, Configure, cancellationToken).ConfigureAwait(false);

        if (onFailure != TenantFailureMode.ReportOnly && result.AnyFailures)
            throw new MigrationFanoutException(result);

        return result;
    }
}
