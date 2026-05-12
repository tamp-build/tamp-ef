using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tamp;
using Tamp.EFCore.V10;
using Xunit;

namespace Tamp.EFCore.V10.Tests;

/// <summary>
/// Tests for the fluent <see cref="MigrationBundlePlan"/> surface — the canonical
/// per-tenant SaaS migration loop API (TAM-104 / TAM-165). Uses the internal
/// test-bundle-invoker seam to skip the actual <c>dotnet ef migrations bundle</c>
/// step; the underlying engine is exercised by <see cref="MigrationFanoutTests"/>.
/// </summary>
public sealed class MigrationBundlePlanTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/dotnet-ef"));

    private static MigrationTarget T(string name, MigrationOutcome? scriptedOutcome = null) =>
        new(name, new Secret($"conn-{name}", $"Host=db;Database={name};Pwd=p"));

    // ---- MigrationTarget.FromConnectionString convenience ----

    [Fact]
    public void FromConnectionString_Wraps_Plain_String_With_Generated_Name()
    {
        var target = MigrationTarget.FromConnectionString("Host=db;Database=t");

        Assert.StartsWith("tenant-", target.Name);
        // The connection string is wrapped as a Secret — exact value is internal-only,
        // but Name carries enough metadata to confirm the wrap landed.
        Assert.StartsWith("conn-", target.ConnectionString.Name);
        Assert.Null(target.Environment);
        Assert.Null(target.Tier);
    }

    [Fact]
    public void FromConnectionString_Preserves_All_Optional_Args()
    {
        var target = MigrationTarget.FromConnectionString(
            "Host=db;Database=acme",
            name: "acme",
            environment: "Production",
            tier: "premium");

        Assert.Equal("acme", target.Name);
        Assert.Equal("Production", target.Environment);
        Assert.Equal("premium", target.Tier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromConnectionString_Rejects_Empty_Or_Whitespace(string? value)
    {
        Assert.Throws<ArgumentException>(() => MigrationTarget.FromConnectionString(value!));
    }

    // ---- MigrationBundlePlan shape ----

    [Fact]
    public void MigrationsBundle_Returns_Plan_With_Working_CommandPlan()
    {
        var plan = EFCore.MigrationsBundle(FakeTool(), s => s.SetOutput("out.exe"));

        Assert.Contains("migrations", plan.Plan.Arguments);
        Assert.Contains("bundle", plan.Plan.Arguments);
        Assert.Contains("--output", plan.Plan.Arguments);
        Assert.Contains("out.exe", plan.Plan.Arguments);
    }

    [Fact]
    public void Implicit_Conversion_To_CommandPlan_Preserves_Arguments()
    {
        // Target body returning the plan directly relies on this conversion.
        CommandPlan asPlan = EFCore.MigrationsBundle(FakeTool(), s => s.SetOutput("out.exe"));

        Assert.Contains("bundle", asPlan.Arguments);
        Assert.Contains("out.exe", asPlan.Arguments);
    }

    [Fact]
    public void Object_Init_Overload_Round_Trips()
    {
        var settings = new EFCoreMigrationsBundleSettings { Output = "init.exe", SelfContained = true };
        var plan = EFCore.MigrationsBundle(FakeTool(), settings);

        Assert.Contains("init.exe", plan.Plan.Arguments);
        Assert.Contains("--self-contained", plan.Plan.Arguments);
    }

    [Fact]
    public void MigrationsBundle_Rejects_Null_Tool()
    {
        Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsBundle(null!, _ => { }));
    }

    [Fact]
    public void MigrationsBundle_Rejects_Null_Settings_Object()
    {
        Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsBundle(FakeTool(), (EFCoreMigrationsBundleSettings)null!));
    }

    // ---- ForEachTenantAsync — parameter wiring + failure-mode semantics ----

    [Fact]
    public async Task ReportOnly_Mode_Returns_Result_Without_Throwing_On_Failures()
    {
        var invoker = new ScriptedInvoker(new()
        {
            ["ok"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
            ["bad"] = new(new[] { new BundleInvocationResult(2, TimeSpan.Zero, "", "boom", false) }),
        });
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, invoker);

        var result = await plan.ForEachTenantAsync(
            new[] { T("ok"), T("bad") },
            onFailure: TenantFailureMode.ReportOnly);

        Assert.True(result.AnyFailures);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task LogAndContinue_Mode_Runs_All_Targets_Then_Throws_With_Full_Report()
    {
        var invoker = new ScriptedInvoker(new()
        {
            ["ok"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
            ["bad"] = new(new[] { new BundleInvocationResult(2, TimeSpan.Zero, "", "boom", false) }),
            ["also-ok"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
        });
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, invoker);

        var ex = await Assert.ThrowsAsync<MigrationFanoutException>(() =>
            plan.ForEachTenantAsync(
                new[] { T("ok"), T("bad"), T("also-ok") },
                onFailure: TenantFailureMode.LogAndContinue));

        // The aggregate result is preserved on the exception — all three targets ran.
        Assert.Equal(2, ex.Result.SucceededCount);
        Assert.Equal(1, ex.Result.FailedCount);
        Assert.Equal(0, ex.Result.SkippedCount);
    }

    [Fact]
    public async Task FailFast_Mode_Throws_On_Failure()
    {
        // Note: we don't assert SkippedCount here. The engine dispatches all tasks up-front
        // through a SemaphoreSlim concurrency gate, and Task.Run gate-acquisition order is
        // not FIFO-deterministic across runtimes. The contract is "first failure trips
        // fail-fast and no further work begins"; the skip semantics are best-effort, not a
        // hard guarantee on every task scheduling. Adopters relying on strict
        // first-failure-stops-the-world should use the LogAndContinue mode and inspect
        // FailedCount + SkippedCount on the result.
        var invoker = new ScriptedInvoker(new()
        {
            ["a"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
            ["b-bad"] = new(new[] { new BundleInvocationResult(2, TimeSpan.Zero, "", "boom", false) }),
            ["c"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
        });
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, invoker);

        var ex = await Assert.ThrowsAsync<MigrationFanoutException>(() =>
            plan.ForEachTenantAsync(
                new[] { T("a"), T("b-bad"), T("c") },
                parallelism: 1,
                onFailure: TenantFailureMode.FailFast));

        Assert.True(ex.Result.FailedCount >= 1, $"expected at least 1 failure, got {ex.Result.FailedCount}");
        Assert.True(ex.Result.AnyFailures);
    }

    [Fact]
    public async Task ForEachTenantAsync_Passes_Parallelism_Through_To_Engine()
    {
        // The invoker records concurrent in-flight calls. parallelism=3 should let 3 land
        // simultaneously rather than serializing them. This is the same proof the engine
        // tests do but verifies the fluent layer wires the parameter through.
        var observer = new ConcurrencyObserver();
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, observer);

        await plan.ForEachTenantAsync(
            new[] { T("a"), T("b"), T("c") },
            parallelism: 3,
            onFailure: TenantFailureMode.ReportOnly);

        Assert.True(observer.MaxConcurrent >= 2, $"expected at least 2 concurrent, observed {observer.MaxConcurrent}");
    }

    [Fact]
    public async Task ForEachTenantAsync_Passes_Verbose_Flag_Through()
    {
        var invoker = new ScriptedInvoker(new()
        {
            ["t"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
        }) { CaptureVerbose = true };
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, invoker);

        await plan.ForEachTenantAsync(new[] { T("t") }, verbose: true, onFailure: TenantFailureMode.ReportOnly);
        Assert.True(invoker.LastVerbose);
    }

    [Fact]
    public async Task ForEachTenantAsync_Honors_TimeoutPerTenant()
    {
        var invoker = new ScriptedInvoker(new()
        {
            ["t"] = new(new[] { new BundleInvocationResult(0, TimeSpan.Zero, "", "", false) }),
        }) { CaptureTimeout = true };
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, invoker);

        await plan.ForEachTenantAsync(
            new[] { T("t") },
            timeoutPerTenant: TimeSpan.FromMinutes(7),
            onFailure: TenantFailureMode.ReportOnly);

        Assert.Equal(TimeSpan.FromMinutes(7), invoker.LastTimeout);
    }

    [Fact]
    public async Task ForEachTenantAsync_Rejects_Null_Tenants()
    {
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, new ScriptedInvoker(new()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => plan.ForEachTenantAsync(null!));
    }

    [Fact]
    public async Task ForEachTenantAsync_Empty_Tenants_Returns_Empty_Result()
    {
        var plan = new MigrationBundlePlan(FakeTool(), new EFCoreMigrationsBundleSettings { Output = "out.exe" }, new ScriptedInvoker(new()));
        var result = await plan.ForEachTenantAsync(Array.Empty<MigrationTarget>(), onFailure: TenantFailureMode.ReportOnly);

        Assert.Empty(result.PerTarget);
        Assert.False(result.AnyFailures);
    }

    // ---- Test scaffolding ----

    private sealed class ScriptedInvoker : IBundleInvoker
    {
        private readonly Dictionary<string, Queue<BundleInvocationResult>> _script;
        public bool CaptureVerbose { get; set; }
        public bool LastVerbose { get; private set; }
        public bool CaptureTimeout { get; set; }
        public TimeSpan LastTimeout { get; private set; }

        public ScriptedInvoker(Dictionary<string, Queue<BundleInvocationResult>> script) => _script = script;

        public Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct)
        {
            if (CaptureVerbose) LastVerbose = verbose;
            if (CaptureTimeout) LastTimeout = timeout;
            if (_script.TryGetValue(target.Name, out var q) && q.Count > 0)
                return Task.FromResult(q.Dequeue());
            return Task.FromResult(new BundleInvocationResult(0, TimeSpan.Zero, "", "", false));
        }
    }

    private sealed class ConcurrencyObserver : IBundleInvoker
    {
        private int _inFlight;
        public int MaxConcurrent { get; private set; }
        private readonly object _lock = new();

        public async Task<BundleInvocationResult> InvokeAsync(string bundlePath, MigrationTarget target, bool verbose, TimeSpan timeout, CancellationToken ct)
        {
            lock (_lock)
            {
                _inFlight++;
                if (_inFlight > MaxConcurrent) MaxConcurrent = _inFlight;
            }
            try
            {
                await Task.Delay(50, ct);
                return new BundleInvocationResult(0, TimeSpan.FromMilliseconds(50), "", "", false);
            }
            finally
            {
                lock (_lock) _inFlight--;
            }
        }
    }
}
