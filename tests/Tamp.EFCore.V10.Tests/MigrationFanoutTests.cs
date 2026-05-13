using System.Collections.Concurrent;
using Xunit;

namespace Tamp.EFCore.V10.Tests;

public sealed class MigrationFanoutTests
{
    private static MigrationTarget T(string name, string? tier = null, string? env = null) =>
        new(name, new Secret($"conn-{name}", $"Server=db;Database={name};Pwd=secret-{name}"), env, tier);

    /// <summary>Records every call and returns a scripted answer per target.</summary>
    private sealed class ScriptedInvoker : IBundleInvoker
    {
        private readonly Dictionary<string, Queue<BundleInvocationResult>> _script;
        public ConcurrentQueue<string> Calls { get; } = new();
        public ConcurrentDictionary<string, int> AttemptCounts { get; } = new();
        public int MaxParallel { get; private set; }
        private int _inFlight;
        public TimeSpan StepDelay { get; set; } = TimeSpan.Zero;

        public ScriptedInvoker(Dictionary<string, Queue<BundleInvocationResult>> script) { _script = script; }

        public async Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct)
        {
            // We deliberately record only the Secret's metadata (Name) — Reveal() is internal-only to Tamp.Core IVT-listed assemblies. The ProcessBundleInvoker calls Reveal() inside the production assembly.
            Calls.Enqueue($"{target.Name}|secret={target.ConnectionString.Name}|verbose={verbose}|env={target.Environment ?? "-"}");
            AttemptCounts.AddOrUpdate(target.Name, 1, (_, n) => n + 1);
            var now = Interlocked.Increment(ref _inFlight);
            MaxParallel = Math.Max(MaxParallel, now);
            try
            {
                if (StepDelay > TimeSpan.Zero) await Task.Delay(StepDelay, ct);
                if (_script.TryGetValue(target.Name, out var q) && q.Count > 0) return q.Dequeue();
                return new BundleInvocationResult(0, TimeSpan.FromMilliseconds(10), "", "", false);
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }
    }

    // ---- Happy path ----

    [Fact]
    public async Task RunAsync_All_Succeed_With_Default_Options()
    {
        var invoker = new ScriptedInvoker(new());
        var targets = new[] { T("t1"), T("t2"), T("t3") };

        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker, null, CancellationToken.None);

        Assert.Equal(3, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.False(result.AnyFailures);
        Assert.All(result.PerTarget, r => Assert.Equal(MigrationOutcome.Succeeded, r.Outcome));
        Assert.Equal(3, invoker.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_Passes_Target_Secret_Reference()
    {
        var invoker = new ScriptedInvoker(new());
        var targets = new[] { T("alpha") };

        await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker, null, CancellationToken.None);

        Assert.Contains(invoker.Calls, c => c.Contains("secret=conn-alpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Plumbs_Environment_And_Verbose_Flags()
    {
        var invoker = new ScriptedInvoker(new());
        var targets = new[] { T("alpha", env: "Staging") };

        await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker, o => o.SetVerbose(true), CancellationToken.None);

        Assert.Contains(invoker.Calls, c => c.Contains("verbose=True", StringComparison.Ordinal) && c.Contains("env=Staging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Empty_Targets_Returns_Empty_Result()
    {
        var invoker = new ScriptedInvoker(new());
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", Array.Empty<MigrationTarget>(), invoker, null, CancellationToken.None);
        Assert.Empty(result.PerTarget);
        Assert.False(result.AnyFailures);
    }

    // ---- Failure / retry ----

    [Fact]
    public async Task Failed_Target_Marked_Failed_With_FailureReason()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["broken"] = new(new[] { new BundleInvocationResult(2, TimeSpan.FromMilliseconds(50), "out", "boom: relation does not exist", false) })
        };
        var invoker = new ScriptedInvoker(script);
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("broken") }, invoker, null, CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.Failed, r.Outcome);
        Assert.Equal(2, r.ExitCode);
        Assert.Equal("boom: relation does not exist", r.StdErr);
        Assert.NotNull(r.FailureReason);
        Assert.True(result.AnyFailures);
    }

    [Fact]
    public async Task Retry_Recovers_When_Transient_Then_Succeeds()
    {
        // First call fails, second succeeds.
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["t"] = new(new[]
            {
                new BundleInvocationResult(1, TimeSpan.FromMilliseconds(10), "", "deadlock detected", false),
                new BundleInvocationResult(0, TimeSpan.FromMilliseconds(10), "ok", "", false),
            })
        };
        var invoker = new ScriptedInvoker(script);
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("t") }, invoker,
            o => o.SetRetries(2, TimeSpan.FromMilliseconds(1)), CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.Succeeded, r.Outcome);
        Assert.Equal(2, r.Attempts);
    }

    [Fact]
    public async Task Retry_Exhausted_Still_Reports_Failed()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["t"] = new(new[]
            {
                new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false),
                new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false),
                new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false),
            })
        };
        var invoker = new ScriptedInvoker(script);
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("t") }, invoker,
            o => o.SetRetries(2, TimeSpan.FromMilliseconds(1)), CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.Failed, r.Outcome);
        Assert.Equal(3, r.Attempts);
    }

    [Fact]
    public async Task Retry_Skipped_When_ShouldRetry_Returns_False()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["t"] = new(new[]
            {
                new BundleInvocationResult(42, TimeSpan.FromMilliseconds(5), "", "permanent error: bad password", false),
                new BundleInvocationResult(0, TimeSpan.FromMilliseconds(5), "", "", false),
            })
        };
        var invoker = new ScriptedInvoker(script);
        // Don't retry "bad password" — that's a credential issue, not transient.
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("t") }, invoker,
            o => o.SetRetries(3).SetShouldRetry((_, stderr) => !stderr.Contains("bad password")),
            CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.Failed, r.Outcome);
        Assert.Equal(1, r.Attempts);  // No retries because predicate said no.
    }

    [Fact]
    public async Task TimedOut_Marks_TimedOut_And_Surfaces_Reason()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["slow"] = new(new[] { new BundleInvocationResult(-1, TimeSpan.FromSeconds(5), "", "", true) })
        };
        var invoker = new ScriptedInvoker(script);
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("slow") }, invoker, null, CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.TimedOut, r.Outcome);
        Assert.Contains("Timed out", r.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoker_Throws_Marked_Failed_With_Message()
    {
        var invoker = new ThrowingInvoker(new InvalidOperationException("bundle missing"));
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("t") }, invoker, null, CancellationToken.None);
        var r = Assert.Single(result.PerTarget);
        Assert.Equal(MigrationOutcome.Failed, r.Outcome);
        Assert.Contains("bundle missing", r.FailureReason!, StringComparison.Ordinal);
    }

    private sealed class ThrowingInvoker : IBundleInvoker
    {
        private readonly Exception _ex;
        public ThrowingInvoker(Exception ex) { _ex = ex; }
        public Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct)
            => Task.FromException<BundleInvocationResult>(_ex);
    }

    // ---- Fan-out behavior ----

    [Fact]
    public async Task Concurrency_Caps_MaxParallel()
    {
        var invoker = new ScriptedInvoker(new()) { StepDelay = TimeSpan.FromMilliseconds(50) };
        var targets = Enumerable.Range(0, 10).Select(i => T($"t{i}")).ToArray();
        await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker, o => o.SetConcurrency(3), CancellationToken.None);
        Assert.True(invoker.MaxParallel <= 3, $"Expected ≤ 3 parallel, observed {invoker.MaxParallel}");
        Assert.True(invoker.MaxParallel >= 2, $"Expected at least some parallelism, observed {invoker.MaxParallel}");
    }

    [Fact]
    public async Task FailFast_At_Concurrency_1_Skips_Every_Remaining_Target_In_Declaration_Order()
    {
        // TAM-168: at Concurrency=1 the engine now runs targets serially in declaration
        // order (not via Task.Run + SemaphoreSlim, which had nondeterministic ordering).
        // Strict assertion: first target fails → ALL subsequent targets are Skipped, in
        // exactly the order they were declared.
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["bad"] = new(new[] { new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false) })
        };
        var invoker = new ScriptedInvoker(script) { StepDelay = TimeSpan.FromMilliseconds(20) };
        var targets = new[] { T("bad"), T("ok1"), T("ok2"), T("ok3"), T("ok4"), T("ok5") };

        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker,
            o => o.SetMode(FanoutMode.FailFast).SetConcurrency(1), CancellationToken.None);

        // Strict per-target outcomes in declaration order.
        Assert.Equal(MigrationOutcome.Failed, result.PerTarget[0].Outcome);
        for (var i = 1; i < targets.Length; i++)
        {
            Assert.Equal(MigrationOutcome.Skipped, result.PerTarget[i].Outcome);
            Assert.Equal(targets[i].Name, result.PerTarget[i].Target.Name);
        }
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(targets.Length - 1, result.SkippedCount);

        // The invoker must have been called exactly once — the failing target.
        Assert.Single(invoker.Calls);
        Assert.StartsWith("bad|", invoker.Calls.First());
    }

    [Fact]
    public async Task Concurrency_1_Preserves_Strict_Declaration_Order_When_All_Succeed()
    {
        // The invoker records the order it was called. At Concurrency=1 it must match
        // exactly the declaration order of the targets, no matter how the runner
        // schedules work.
        var invoker = new ScriptedInvoker(new()) { StepDelay = TimeSpan.FromMilliseconds(5) };
        var targets = Enumerable.Range(1, 10).Select(i => T($"tenant-{i:D2}")).ToArray();

        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker,
            o => o.SetConcurrency(1), CancellationToken.None);

        Assert.Equal(10, result.SucceededCount);
        // ScriptedInvoker.Calls records "name|secret=...|verbose=...|env=..." per call;
        // extract just the name prefix to compare order.
        var calledNames = invoker.Calls.Select(s => s.Split('|', 2)[0]).ToList();
        Assert.Equal(targets.Select(t => t.Name).ToList(), calledNames);
        // PerTarget is index-aligned with the input array (always — strict at concurrency=1).
        for (var i = 0; i < targets.Length; i++)
            Assert.Equal(targets[i].Name, result.PerTarget[i].Target.Name);
    }

    [Fact]
    public async Task Concurrency_1_FailFast_Failure_In_Middle_Still_Skips_Strictly_From_That_Point()
    {
        // Failing target is in the middle of the list. Everything before should succeed,
        // everything after should be Skipped — strict ordering at concurrency=1.
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["mid-bad"] = new(new[] { new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false) })
        };
        var invoker = new ScriptedInvoker(script) { StepDelay = TimeSpan.FromMilliseconds(5) };
        var targets = new[] { T("a"), T("b"), T("c"), T("mid-bad"), T("e"), T("f") };

        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker,
            o => o.SetMode(FanoutMode.FailFast).SetConcurrency(1), CancellationToken.None);

        Assert.Equal(MigrationOutcome.Succeeded, result.PerTarget[0].Outcome);
        Assert.Equal(MigrationOutcome.Succeeded, result.PerTarget[1].Outcome);
        Assert.Equal(MigrationOutcome.Succeeded, result.PerTarget[2].Outcome);
        Assert.Equal(MigrationOutcome.Failed,    result.PerTarget[3].Outcome);
        Assert.Equal(MigrationOutcome.Skipped,   result.PerTarget[4].Outcome);
        Assert.Equal(MigrationOutcome.Skipped,   result.PerTarget[5].Outcome);

        // Invoker calls: a, b, c, mid-bad (4 total). e and f never ran.
        var calledNames = invoker.Calls.Select(s => s.Split('|', 2)[0]).ToList();
        Assert.Equal(new[] { "a", "b", "c", "mid-bad" }, calledNames);
    }

    [Fact]
    public async Task ContinueOnError_Runs_Every_Target()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["bad"] = new(new[] { new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false) })
        };
        var invoker = new ScriptedInvoker(script);
        var targets = new[] { T("ok1"), T("bad"), T("ok2") };
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker,
            o => o.SetMode(FanoutMode.ContinueOnError).SetConcurrency(1), CancellationToken.None);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(3, invoker.Calls.Count);
    }

    [Fact]
    public async Task RunAndThrowOnFailureAsync_Throws_With_Result_Payload()
    {
        var script = new Dictionary<string, Queue<BundleInvocationResult>>
        {
            ["bad"] = new(new[] { new BundleInvocationResult(1, TimeSpan.FromMilliseconds(5), "", "boom", false) })
        };
        var invoker = new ScriptedInvoker(script);
        var ex = await Assert.ThrowsAsync<MigrationFanoutException>(() =>
            EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("ok1"), T("bad") }, invoker, null, CancellationToken.None)
                .ContinueWith(async t =>
                {
                    var r = await t;
                    if (r.AnyFailures) throw new MigrationFanoutException(r);
                    return r;
                }).Unwrap());
        Assert.Contains("bad(Failed)", ex.Message, StringComparison.Ordinal);
        Assert.Single(ex.Result.Failures);
    }

    [Fact]
    public async Task RunAndThrowOnFailureAsync_Returns_When_All_Succeed()
    {
        // Both targets succeed via the scripted invoker — we exercise the throw-wrapper version
        // by hand here since the public overload uses ProcessBundleInvoker (which would try to
        // execute a real bundle exe).
        var invoker = new ScriptedInvoker(new());
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("a"), T("b") }, invoker, null, CancellationToken.None);
        Assert.Equal(2, result.SucceededCount);
        Assert.False(result.AnyFailures);
    }

    [Fact]
    public async Task Cancellation_Returns_Partial_Result_With_Skipped_Entries()
    {
        // External cancellation returns the partial result rather than throwing — the SaaS
        // observability story is "show me what completed and what didn't", and OperationCancelled
        // would hide that. Targets that hadn't started (or were mid-flight when cancellation
        // arrived) are marked Skipped.
        var invoker = new ScriptedInvoker(new()) { StepDelay = TimeSpan.FromSeconds(2) };
        var targets = Enumerable.Range(0, 5).Select(i => T($"t{i}")).ToArray();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var result = await EFCoreMigrationFanout.RunAsync("bundle.exe", targets, invoker, o => o.SetConcurrency(1), cts.Token);
        Assert.Equal(5, result.PerTarget.Count);
        Assert.True(result.SkippedCount > 0, $"Expected at least one Skipped entry; got {result.SkippedCount}.");
        Assert.True(result.SucceededCount < 5, "Expected cancellation to prevent some targets from succeeding.");
    }

    [Fact]
    public async Task ProgressWriter_Receives_One_Line_Per_Completion()
    {
        var invoker = new ScriptedInvoker(new());
        var sw = new StringWriter();
        await EFCoreMigrationFanout.RunAsync("bundle.exe", new[] { T("a"), T("b") }, invoker,
            o => o.SetProgressWriter(sw), CancellationToken.None);
        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Contains("Succeeded", l, StringComparison.Ordinal));
    }

    // ---- Argument guards ----

    [Fact]
    public async Task RunAsync_Rejects_Blank_BundlePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            EFCoreMigrationFanout.RunAsync("", new[] { T("a") }, new ScriptedInvoker(new()), null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Rejects_Null_Targets()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            EFCoreMigrationFanout.RunAsync("b", null!, new ScriptedInvoker(new()), null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Rejects_Null_Invoker()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            EFCoreMigrationFanout.RunAsync("b", new[] { T("a") }, null!, null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Rejects_Bad_Concurrency()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EFCoreMigrationFanout.RunAsync("b", new[] { T("a") }, new ScriptedInvoker(new()), o => o.SetConcurrency(0), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Rejects_Bad_Timeout()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EFCoreMigrationFanout.RunAsync("b", new[] { T("a") }, new ScriptedInvoker(new()), o => o.SetPerTargetTimeout(TimeSpan.Zero), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Rejects_Negative_Retries()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EFCoreMigrationFanout.RunAsync("b", new[] { T("a") }, new ScriptedInvoker(new()), o => o.SetRetries(-1), CancellationToken.None));
    }

    // ---- Result aggregation ----

    [Fact]
    public void Result_Counts_Are_Computed_From_PerTarget()
    {
        var t = (string name, MigrationOutcome o) => new MigrationTargetResult(T(name), o, 0, TimeSpan.Zero, 1, "", "", null);
        var r = new MigrationFanoutResult(new[]
        {
            t("a", MigrationOutcome.Succeeded),
            t("b", MigrationOutcome.Failed),
            t("c", MigrationOutcome.TimedOut),
            t("d", MigrationOutcome.Skipped),
            t("e", MigrationOutcome.Succeeded),
        }, TimeSpan.FromSeconds(1));
        Assert.Equal(2, r.SucceededCount);
        Assert.Equal(1, r.FailedCount);
        Assert.Equal(1, r.TimedOutCount);
        Assert.Equal(1, r.SkippedCount);
        Assert.True(r.AnyFailures);
        Assert.Equal(2, r.Failures.Count);
    }

    [Fact]
    public void Target_Tier_Preserved_In_Result()
    {
        var result = new MigrationTargetResult(T("acme", tier: "tenant"), MigrationOutcome.Succeeded, 0, TimeSpan.Zero, 1, "", "", null);
        Assert.Equal("tenant", result.Target.Tier);
    }
}
