using System.Diagnostics;
using System.Text;
using Tamp;

namespace Tamp.EFCore.V10;

/// <summary>Identifies a single migration target — one row in the per-tenant SaaS loop.</summary>
/// <param name="Name">Human-readable label for logs / reports (e.g. tenant slug or shard ID).</param>
/// <param name="ConnectionString">DB connection string, redacted in logs via the <see cref="Secret"/> contract.</param>
/// <param name="Environment">Value to pass as <c>ASPNETCORE_ENVIRONMENT</c> when invoking the bundle. Null leaves the bundle's compile-time default.</param>
/// <param name="Tier">Optional grouping label. SaaS shapes often have multiple tiers (master / regional / per-tenant). Surfaced in <see cref="MigrationTargetResult"/> so reports can group by it.</param>
public sealed record MigrationTarget(
    string Name,
    Secret ConnectionString,
    string? Environment = null,
    string? Tier = null)
{
    /// <summary>
    /// Convenience for the explicit-list case: the adopter already has a plain connection
    /// string and just needs it wrapped as a <see cref="MigrationTarget"/>. How you derive
    /// the tenant catalog (master-DB query, KV lookup, hard-coded list) is project-specific —
    /// Tamp does not opine. This helper just saves the three-line wrapper at the call site.
    /// </summary>
    /// <param name="connectionString">Plain connection string. Will be wrapped in a <see cref="Secret"/>.</param>
    /// <param name="name">Tenant name for logs / reports. Defaults to a sequential placeholder.</param>
    /// <param name="environment">Optional <c>ASPNETCORE_ENVIRONMENT</c> override for the bundle invocation.</param>
    /// <param name="tier">Optional tier label for grouped reporting.</param>
    public static MigrationTarget FromConnectionString(
        string connectionString,
        string? name = null,
        string? environment = null,
        string? tier = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));
        var resolvedName = name ?? $"tenant-{Guid.NewGuid().ToString("N")[..8]}";
        return new MigrationTarget(
            resolvedName,
            new Secret($"conn-{resolvedName}", connectionString),
            environment,
            tier);
    }

    /// <summary>
    /// Secret-typed overload of <see cref="FromConnectionString(string, string?, string?, string?)"/>.
    /// Adopters carrying tenant connection strings as <see cref="Secret"/> (the canonical shape for
    /// credentialed material) use this overload to preserve redaction through the build pipeline.
    /// The supplied Secret's <see cref="Secret.Name"/> is preserved if no <paramref name="name"/>
    /// is given (it's typically already meaningful — e.g. "TENANT_3__CONNECTION").
    /// </summary>
    /// <param name="connectionString">Connection string already wrapped as a <see cref="Secret"/>.</param>
    /// <param name="name">Tenant name for logs / reports. Defaults to the Secret's Name.</param>
    /// <param name="environment">Optional <c>ASPNETCORE_ENVIRONMENT</c> override.</param>
    /// <param name="tier">Optional tier label.</param>
    public static MigrationTarget FromConnectionString(
        Secret connectionString,
        string? name = null,
        string? environment = null,
        string? tier = null)
    {
        if (connectionString is null) throw new ArgumentNullException(nameof(connectionString));
        var resolvedName = name ?? connectionString.Name;
        return new MigrationTarget(resolvedName, connectionString, environment, tier);
    }
}

/// <summary>Outcome of one target in a migration fan-out.</summary>
public enum MigrationOutcome
{
    /// <summary>Bundle exited 0 — pending migrations applied (or none were pending).</summary>
    Succeeded,
    /// <summary>Bundle exited non-zero or threw — see <see cref="MigrationTargetResult.FailureReason"/>.</summary>
    Failed,
    /// <summary>Bundle ran longer than the per-target timeout and was killed.</summary>
    TimedOut,
    /// <summary>Fail-fast tripped on a previous target — this one never ran.</summary>
    Skipped,
}

/// <summary>Mode for handling per-target failures in the fan-out.</summary>
public enum FanoutMode
{
    /// <summary>On first failure, cancel remaining targets and return — they're marked <see cref="MigrationOutcome.Skipped"/>.</summary>
    FailFast,
    /// <summary>Run every target regardless of failures; the result aggregates all outcomes.</summary>
    ContinueOnError,
}

/// <summary>Result for a single migration target.</summary>
public sealed record MigrationTargetResult(
    MigrationTarget Target,
    MigrationOutcome Outcome,
    int ExitCode,
    TimeSpan Duration,
    int Attempts,
    string StdOut,
    string StdErr,
    string? FailureReason)
{
    public bool IsSuccess => Outcome == MigrationOutcome.Succeeded;
}

/// <summary>Aggregate result for the whole fan-out. Use the count properties for dashboards / alerting.</summary>
public sealed record MigrationFanoutResult(
    IReadOnlyList<MigrationTargetResult> PerTarget,
    TimeSpan TotalDuration)
{
    public int SucceededCount => PerTarget.Count(r => r.Outcome == MigrationOutcome.Succeeded);
    public int FailedCount => PerTarget.Count(r => r.Outcome == MigrationOutcome.Failed);
    public int TimedOutCount => PerTarget.Count(r => r.Outcome == MigrationOutcome.TimedOut);
    public int SkippedCount => PerTarget.Count(r => r.Outcome == MigrationOutcome.Skipped);
    public bool AnyFailures => FailedCount + TimedOutCount > 0;
    public IReadOnlyList<MigrationTargetResult> Failures => PerTarget.Where(r => !r.IsSuccess && r.Outcome != MigrationOutcome.Skipped).ToList();
}

/// <summary>Thrown by <see cref="EFCoreMigrationFanout.RunAndThrowOnFailureAsync"/> when any target failed or timed out.</summary>
public sealed class MigrationFanoutException : Exception
{
    public MigrationFanoutResult Result { get; }
    public MigrationFanoutException(MigrationFanoutResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    private static string BuildMessage(MigrationFanoutResult result)
    {
        var failureCount = result.FailedCount + result.TimedOutCount;
        var names = string.Join(", ", result.Failures.Select(f => $"{f.Target.Name}({f.Outcome})"));
        return System.FormattableString.Invariant($"Migration fan-out had {failureCount} failure(s): {names}");
    }
}

/// <summary>
/// Options for <see cref="EFCoreMigrationFanout.RunAsync"/>. SaaS-tuned defaults: serial execution (concurrency 1),
/// 5-minute per-target timeout, fail-fast off so you get a full report instead of stopping at the first bad tenant.
/// </summary>
public sealed class MigrationFanoutOptions
{
    public int Concurrency { get; set; } = 1;
    public TimeSpan PerTargetTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public FanoutMode Mode { get; set; } = FanoutMode.ContinueOnError;
    /// <summary>How many times to re-attempt a target after a failure. 0 = no retries. Backoff between attempts is <see cref="RetryDelay"/>.</summary>
    public int MaxRetries { get; set; }
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Predicate: should this failure be retried? Default = retry every failure up to <see cref="MaxRetries"/>.</summary>
    public Func<int /*exitCode*/, string /*stdErr*/, bool> ShouldRetry { get; set; } = (_, _) => true;
    /// <summary>If set, the fan-out writes a one-line progress entry per target completion (start / end / outcome).</summary>
    public TextWriter? ProgressWriter { get; set; }
    /// <summary>Verbose flag passed through to the bundle invocation (<c>--verbose</c>).</summary>
    public bool Verbose { get; set; }

    public MigrationFanoutOptions SetConcurrency(int n) { Concurrency = n; return this; }
    public MigrationFanoutOptions SetPerTargetTimeout(TimeSpan t) { PerTargetTimeout = t; return this; }
    public MigrationFanoutOptions SetMode(FanoutMode mode) { Mode = mode; return this; }
    public MigrationFanoutOptions SetRetries(int max, TimeSpan? delay = null)
    {
        MaxRetries = max;
        if (delay is { } d) RetryDelay = d;
        return this;
    }
    public MigrationFanoutOptions SetShouldRetry(Func<int, string, bool> predicate) { ShouldRetry = predicate; return this; }
    public MigrationFanoutOptions SetProgressWriter(TextWriter? writer) { ProgressWriter = writer; return this; }
    public MigrationFanoutOptions SetVerbose(bool v = true) { Verbose = v; return this; }
}

/// <summary>
/// Test seam: invokes one migration bundle against one target and returns the captured result.
/// Production implementation shells out to the bundle exe; tests replace this with a fake.
/// </summary>
public interface IBundleInvoker
{
    Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Raw result of a single bundle invocation, before fan-out-level retry / outcome classification.</summary>
public sealed record BundleInvocationResult(int ExitCode, TimeSpan Duration, string StdOut, string StdErr, bool TimedOut);

internal sealed class ProcessBundleInvoker : IBundleInvoker
{
    public async Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = bundlePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--connection");
        // TODO: extract into MigrationFanoutSettings to satisfy TAMP004 cleanly.
#pragma warning disable TAMP004
        psi.ArgumentList.Add(target.ConnectionString.Reveal());
#pragma warning restore TAMP004
        if (verbose) psi.ArgumentList.Add("--verbose");
        if (target.Environment is { Length: > 0 } env)
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = env;

        using var proc = new Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdOut) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stdErr) stdErr.AppendLine(e.Data); };

        var sw = Stopwatch.StartNew();
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        sw.Stop();

        return new BundleInvocationResult(
            ExitCode: timedOut ? -1 : proc.ExitCode,
            Duration: sw.Elapsed,
            StdOut: stdOut.ToString(),
            StdErr: stdErr.ToString(),
            TimedOut: timedOut);
    }
}

/// <summary>
/// Orchestrates running one EF Core migration bundle against N targets — the per-tenant SaaS migration loop.
/// </summary>
/// <remarks>
/// <para>
/// The bundle is produced by <see cref="EFCore.MigrationsBundle"/> and is a self-contained executable. It accepts
/// <c>--connection &lt;connstr&gt;</c> and (optionally) <c>--verbose</c>; it reads <c>ASPNETCORE_ENVIRONMENT</c> from
/// the environment. The fan-out invokes it once per target with that target's connection string.
/// </para>
/// <para>
/// Concurrency &gt; 1 means parallel writes against potentially-shared DB infrastructure. Set it deliberately —
/// 4-8 is typical for Postgres-per-tenant shapes, 1 is right when migrations include <c>CREATE INDEX</c> on shared
/// tablespaces.
/// </para>
/// </remarks>
public static class EFCoreMigrationFanout
{
    /// <summary>Run the bundle against every target. Returns a structured result; does NOT throw on per-target failures.</summary>
    public static Task<MigrationFanoutResult> RunAsync(
        string bundlePath,
        IReadOnlyList<MigrationTarget> targets,
        Action<MigrationFanoutOptions>? configure = null,
        CancellationToken ct = default)
        => RunAsync(bundlePath, targets, new ProcessBundleInvoker(), configure, ct);

    /// <summary>Run the bundle and throw <see cref="MigrationFanoutException"/> on any per-target failure or timeout.</summary>
    public static async Task<MigrationFanoutResult> RunAndThrowOnFailureAsync(
        string bundlePath,
        IReadOnlyList<MigrationTarget> targets,
        Action<MigrationFanoutOptions>? configure = null,
        CancellationToken ct = default)
    {
        var result = await RunAsync(bundlePath, targets, configure, ct).ConfigureAwait(false);
        if (result.AnyFailures) throw new MigrationFanoutException(result);
        return result;
    }

    internal static async Task<MigrationFanoutResult> RunAsync(
        string bundlePath,
        IReadOnlyList<MigrationTarget> targets,
        IBundleInvoker invoker,
        Action<MigrationFanoutOptions>? configure,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bundlePath)) throw new ArgumentException("Bundle path must not be null or whitespace.", nameof(bundlePath));
        if (targets is null) throw new ArgumentNullException(nameof(targets));
        if (invoker is null) throw new ArgumentNullException(nameof(invoker));

        var options = new MigrationFanoutOptions();
        configure?.Invoke(options);
        if (options.Concurrency < 1) throw new ArgumentOutOfRangeException(nameof(configure), "Concurrency must be ≥ 1.");
        if (options.PerTargetTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(configure), "PerTargetTimeout must be positive.");
        if (options.MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(configure), "MaxRetries must be ≥ 0.");

        if (targets.Count == 0) return new MigrationFanoutResult(Array.Empty<MigrationTargetResult>(), TimeSpan.Zero);

        var totalSw = Stopwatch.StartNew();
        var results = new MigrationTargetResult?[targets.Count];

        using var failFastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (options.Concurrency == 1)
        {
            // ── Serial path (concurrency=1) ───────────────────────────────────
            // Targets execute in strict declaration order, one at a time, with no
            // thread-pool dispatch indeterminism. Tests can therefore assert STRICT
            // contracts: "target N+1 starts only after target N completes", and
            // "first failure under FailFast → all subsequent results are Skipped".
            //
            // TAM-168: the previous engine path (Task.Run + SemaphoreSlim with
            // permit=1) appeared serial but was actually order-non-deterministic
            // — the thread pool could dispatch queued tasks in any order even when
            // only one held the semaphore at a time. v0.3.0 had to ship with the
            // test assertions loosened to "weak ordering" to mask flakes. This
            // branch makes the strict contract real.
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];

                if (failFastCts.IsCancellationRequested)
                {
                    results[i] = new MigrationTargetResult(target, MigrationOutcome.Skipped, 0, TimeSpan.Zero, 0, "", "", "Skipped after fail-fast tripped.");
                    continue;
                }

                try
                {
                    var result = await RunOneAsync(invoker, bundlePath, target, options, failFastCts.Token).ConfigureAwait(false);
                    results[i] = result;
                    options.ProgressWriter?.WriteLine($"  [{target.Name}] {result.Outcome} ({result.Duration.TotalSeconds:0.0}s, exit={result.ExitCode}, attempts={result.Attempts})");

                    if (!result.IsSuccess && options.Mode == FanoutMode.FailFast)
                    {
                        failFastCts.Cancel();
                    }
                }
                catch (OperationCanceledException)
                {
                    results[i] = new MigrationTargetResult(target, MigrationOutcome.Skipped, 0, TimeSpan.Zero, 0, "", "", "Cancelled.");
                }
            }
        }
        else
        {
            // ── Parallel path (concurrency > 1) ───────────────────────────────
            // Multiple targets in flight concurrently. Declaration order is not
            // preserved (deliberately — there are N permits and the OS scheduler
            // picks the dispatch order). Tests at concurrency > 1 should assert
            // weak contracts only.
            var failFastTripped = 0;

            using var gate = new SemaphoreSlim(options.Concurrency);
            var tasks = new List<Task>(targets.Count);

            for (var i = 0; i < targets.Count; i++)
            {
                var index = i;
                var target = targets[index];
                tasks.Add(Task.Run(async () =>
                {
                    if (failFastCts.IsCancellationRequested)
                    {
                        results[index] = new MigrationTargetResult(target, MigrationOutcome.Skipped, ExitCode: 0, Duration: TimeSpan.Zero, Attempts: 0, StdOut: "", StdErr: "", FailureReason: "Skipped after fail-fast tripped.");
                        return;
                    }

                    await gate.WaitAsync(failFastCts.Token).ConfigureAwait(false);
                    try
                    {
                        if (failFastCts.IsCancellationRequested)
                        {
                            results[index] = new MigrationTargetResult(target, MigrationOutcome.Skipped, 0, TimeSpan.Zero, 0, "", "", "Skipped after fail-fast tripped.");
                            return;
                        }
                        var result = await RunOneAsync(invoker, bundlePath, target, options, failFastCts.Token).ConfigureAwait(false);
                        results[index] = result;
                        options.ProgressWriter?.WriteLine($"  [{target.Name}] {result.Outcome} ({result.Duration.TotalSeconds:0.0}s, exit={result.ExitCode}, attempts={result.Attempts})");

                        if (!result.IsSuccess && options.Mode == FanoutMode.FailFast
                            && Interlocked.Exchange(ref failFastTripped, 1) == 0)
                        {
                            failFastCts.Cancel();
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, failFastCts.Token));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Both flavors of cancellation (caller's ct OR fail-fast) end the fan-out early.
                // Either way we want the partial result — that's the SaaS observability story.
                // Targets that never finished get backfilled below as Skipped.
            }
        }

        // Backfill any tasks that were cancelled before they recorded a result.
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i] is null)
                results[i] = new MigrationTargetResult(targets[i], MigrationOutcome.Skipped, 0, TimeSpan.Zero, 0, "", "", "Skipped after fail-fast tripped.");
        }

        totalSw.Stop();
        return new MigrationFanoutResult(results.Select(r => r!).ToList(), totalSw.Elapsed);
    }

    private static async Task<MigrationTargetResult> RunOneAsync(
        IBundleInvoker invoker,
        string bundlePath,
        MigrationTarget target,
        MigrationFanoutOptions options,
        CancellationToken ct)
    {
        var attempts = 0;
        BundleInvocationResult? last = null;
        Exception? lastException = null;
        var totalDuration = TimeSpan.Zero;

        while (true)
        {
            attempts++;
            try
            {
                last = await invoker.InvokeAsync(bundlePath, target, options.Verbose, options.PerTargetTimeout, ct).ConfigureAwait(false);
                totalDuration += last.Duration;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new MigrationTargetResult(target, MigrationOutcome.Skipped, 0, totalDuration, attempts, last?.StdOut ?? "", last?.StdErr ?? "", "Cancelled.");
            }
            catch (Exception ex)
            {
                lastException = ex;
                last = null;
            }

            if (last is { TimedOut: true })
            {
                if (attempts <= options.MaxRetries && !ct.IsCancellationRequested)
                {
                    await Task.Delay(options.RetryDelay, ct).ConfigureAwait(false);
                    continue;
                }
                return new MigrationTargetResult(target, MigrationOutcome.TimedOut, last.ExitCode, totalDuration, attempts, last.StdOut, last.StdErr, $"Timed out after {options.PerTargetTimeout}.");
            }

            if (last is { ExitCode: 0 })
                return new MigrationTargetResult(target, MigrationOutcome.Succeeded, 0, totalDuration, attempts, last.StdOut, last.StdErr, null);

            // Failure path — possibly retry.
            var exitCode = last?.ExitCode ?? -1;
            var stdErr = last?.StdErr ?? lastException?.Message ?? "";
            if (attempts <= options.MaxRetries && options.ShouldRetry(exitCode, stdErr) && !ct.IsCancellationRequested)
            {
                await Task.Delay(options.RetryDelay, ct).ConfigureAwait(false);
                continue;
            }
            var reason = lastException is not null ? $"Invoker threw: {lastException.Message}" : $"Bundle exited {exitCode}.";
            return new MigrationTargetResult(target, MigrationOutcome.Failed, exitCode, totalDuration, attempts, last?.StdOut ?? "", stdErr, reason);
        }
    }

}
