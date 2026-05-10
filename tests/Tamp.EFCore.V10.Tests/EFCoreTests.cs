using Bogus;
using Xunit;

namespace Tamp.EFCore.V10.Tests;

public sealed class EFCoreTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/dotnet-ef"));

    private static int IndexOf(IReadOnlyList<string> args, string value, int start = 0)
    {
        for (var i = start; i < args.Count; i++)
            if (args[i] == value) return i;
        return -1;
    }

    private static Faker Faker(int seed = 0xCAFE)
        => new() { Random = new Bogus.Randomizer(seed) };

    // ================================================================
    // Cross-cutting: every verb rejects null tool
    // ================================================================

    [Fact]
    public void DatabaseUpdate_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DatabaseUpdate(null!));

    [Fact]
    public void DatabaseDrop_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DatabaseDrop(null!));

    [Fact]
    public void DbContextInfo_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextInfo(null!));

    [Fact]
    public void DbContextList_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextList(null!));

    [Fact]
    public void DbContextOptimize_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextOptimize(null!));

    [Fact]
    public void DbContextScript_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextScript(null!));

    [Fact]
    public void DbContextScaffold_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextScaffold(null!, _ => { }));

    [Fact]
    public void DbContextScaffold_Throws_On_Null_Configurer()
        => Assert.Throws<ArgumentNullException>(() => EFCore.DbContextScaffold(FakeTool(), null!));

    [Fact]
    public void MigrationsAdd_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsAdd(null!, _ => { }));

    [Fact]
    public void MigrationsAdd_Throws_On_Null_Configurer()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsAdd(FakeTool(), null!));

    [Fact]
    public void MigrationsRemove_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsRemove(null!));

    [Fact]
    public void MigrationsList_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsList(null!));

    [Fact]
    public void MigrationsScript_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsScript(null!));

    [Fact]
    public void MigrationsBundle_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsBundle(null!));

    [Fact]
    public void MigrationsHasPendingModelChanges_Throws_On_Null_Tool()
        => Assert.Throws<ArgumentNullException>(() => EFCore.MigrationsHasPendingModelChanges(null!));

    // ================================================================
    // Cross-cutting: every verb resolves Executable to the tool path
    // ================================================================

    [Theory]
    [InlineData("/usr/local/bin/dotnet-ef")]
    [InlineData("/Users/scott/.dotnet/tools/dotnet-ef")]
    [InlineData("/path with spaces/dotnet-ef")]
    public void Every_Verb_Uses_Tool_Path_As_Executable(string toolPath)
    {
        var tool = new Tool(AbsolutePath.Create(toolPath));
        Assert.Equal(toolPath, EFCore.DatabaseUpdate(tool).Executable);
        Assert.Equal(toolPath, EFCore.DatabaseDrop(tool).Executable);
        Assert.Equal(toolPath, EFCore.DbContextInfo(tool).Executable);
        Assert.Equal(toolPath, EFCore.DbContextList(tool).Executable);
        Assert.Equal(toolPath, EFCore.DbContextOptimize(tool).Executable);
        Assert.Equal(toolPath, EFCore.DbContextScript(tool).Executable);
        Assert.Equal(toolPath, EFCore.MigrationsRemove(tool).Executable);
        Assert.Equal(toolPath, EFCore.MigrationsList(tool).Executable);
        Assert.Equal(toolPath, EFCore.MigrationsScript(tool).Executable);
        Assert.Equal(toolPath, EFCore.MigrationsBundle(tool).Executable);
        Assert.Equal(toolPath, EFCore.MigrationsHasPendingModelChanges(tool).Executable);
    }

    // ================================================================
    // database update
    // ================================================================

    [Fact]
    public void DatabaseUpdate_Bare_Args_Are_Verb_Tokens()
    {
        var args = EFCore.DatabaseUpdate(FakeTool()).Arguments;
        Assert.Equal("database", args[0]);
        Assert.Equal("update", args[1]);
    }

    [Fact]
    public void DatabaseUpdate_TargetMigration_Is_Positional_After_Verb()
    {
        var args = EFCore.DatabaseUpdate(FakeTool(), s => s.SetTargetMigration("0")).Arguments;
        Assert.Equal("database", args[0]);
        Assert.Equal("update", args[1]);
        Assert.Equal("0", args[2]);
    }

    [Fact]
    public void DatabaseUpdate_Connection_Emits_Flag_With_Revealed_Value_And_Registers_Secret()
    {
        var conn = new Secret("ConnectionString", "Server=db;User=sa;Password=hunter2");
        var plan = EFCore.DatabaseUpdate(FakeTool(), s => s.SetConnection(conn));
        var args = plan.Arguments;
        Assert.Equal("Server=db;User=sa;Password=hunter2", args[IndexOf(args, "--connection") + 1]);
        Assert.Single(plan.Secrets);
        Assert.Same(conn, plan.Secrets[0]);
    }

    [Fact]
    public void DatabaseUpdate_Has_No_DryRun_Setter()
    {
        // Surface-policing: integration tests against EF 10.0.7 confirmed
        // that `database update` does NOT accept --dry-run (only
        // `database drop` does). The wrapper must not expose a setter
        // for a flag the CLI rejects.
        var setters = typeof(EFCoreDatabaseUpdateSettings)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Set"))
            .Select(m => m.Name)
            .ToHashSet();
        Assert.DoesNotContain("SetDryRun", setters);
    }

    [Fact]
    public void DatabaseUpdate_Context_Emits_Flag_Pair()
    {
        var args = EFCore.DatabaseUpdate(FakeTool(), s => s.SetContext("AppDbContext")).Arguments;
        Assert.Equal("AppDbContext", args[IndexOf(args, "--context") + 1]);
    }

    [Fact]
    public void DatabaseUpdate_With_No_Connection_Has_Empty_Secrets()
    {
        var plan = EFCore.DatabaseUpdate(FakeTool());
        Assert.Empty(plan.Secrets);
    }

    // ================================================================
    // database drop
    // ================================================================

    [Fact]
    public void DatabaseDrop_Force_DryRun_Context_Round_Trip()
    {
        var plan = EFCore.DatabaseDrop(FakeTool(), s => s.SetForce().SetDryRun().SetContext("Ctx"));
        var args = plan.Arguments;
        Assert.Equal("database", args[0]);
        Assert.Equal("drop", args[1]);
        Assert.Contains("--force", args);
        Assert.Contains("--dry-run", args);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
        Assert.Empty(plan.Secrets);
    }

    [Fact]
    public void DatabaseDrop_Has_No_Connection_Setter()
    {
        // Surface-policing: EF 10.0.7's `database drop` does NOT accept
        // --connection. The connection comes from OnConfiguring /
        // AddDbContext registration on the startup project.
        var setters = typeof(EFCoreDatabaseDropSettings)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Set"))
            .Select(m => m.Name)
            .ToHashSet();
        Assert.DoesNotContain("SetConnection", setters);
    }

    // ================================================================
    // dbcontext info / list / optimize / script
    // ================================================================

    [Fact]
    public void DbContextInfo_Verb_Tokens_Then_Optional_Context()
    {
        var args = EFCore.DbContextInfo(FakeTool(), s => s.SetContext("AppCtx")).Arguments;
        Assert.Equal(new[] { "dbcontext", "info" }, args.Take(2));
        Assert.Equal("AppCtx", args[IndexOf(args, "--context") + 1]);
    }

    [Fact]
    public void DbContextList_Bare_Has_Only_Verb_Tokens()
    {
        var args = EFCore.DbContextList(FakeTool()).Arguments;
        Assert.Equal(new[] { "dbcontext", "list" }, args.ToArray());
    }

    [Fact]
    public void DbContextOptimize_Full_Surface_Round_Trip()
    {
        var args = EFCore.DbContextOptimize(FakeTool(), s => s
            .SetOutputDir("Generated")
            .SetNamespace("MyApp.Compiled")
            .SetContext("AppCtx")
            .SetSuffix(".g")
            .SetPrecompileQueries()
            .SetNativeAot()).Arguments;
        Assert.Equal(new[] { "dbcontext", "optimize" }, args.Take(2));
        Assert.Equal("Generated", args[IndexOf(args, "--output-dir") + 1]);
        Assert.Equal("MyApp.Compiled", args[IndexOf(args, "--namespace") + 1]);
        Assert.Equal("AppCtx", args[IndexOf(args, "--context") + 1]);
        Assert.Equal(".g", args[IndexOf(args, "--suffix") + 1]);
        Assert.Contains("--precompile-queries", args);
        Assert.Contains("--nativeaot", args);
    }

    [Fact]
    public void DbContextScript_Output_And_Context_Round_Trip()
    {
        var args = EFCore.DbContextScript(FakeTool(), s => s
            .SetOutput("schema.sql")
            .SetContext("AppCtx")).Arguments;
        Assert.Equal(new[] { "dbcontext", "script" }, args.Take(2));
        Assert.Equal("schema.sql", args[IndexOf(args, "--output") + 1]);
        Assert.Equal("AppCtx", args[IndexOf(args, "--context") + 1]);
    }

    // ================================================================
    // dbcontext scaffold (REQUIRED: connection, provider — both positional)
    // ================================================================

    [Fact]
    public void DbContextScaffold_Throws_When_Connection_Missing()
        => Assert.Throws<InvalidOperationException>(() => EFCore.DbContextScaffold(FakeTool(), s => s.SetProvider("X")));

    [Fact]
    public void DbContextScaffold_Throws_When_Provider_Missing()
    {
        var conn = new Secret("Conn", "abc");
        Assert.Throws<InvalidOperationException>(() => EFCore.DbContextScaffold(FakeTool(), s => s.SetConnection(conn)));
    }

    [Fact]
    public void DbContextScaffold_Connection_And_Provider_Are_Positional_Right_After_Verb()
    {
        var conn = new Secret("Conn", "Server=db;Pwd=p");
        var args = EFCore.DbContextScaffold(FakeTool(), s => s
            .SetConnection(conn)
            .SetProvider("Microsoft.EntityFrameworkCore.SqlServer")).Arguments;
        Assert.Equal("dbcontext", args[0]);
        Assert.Equal("scaffold", args[1]);
        Assert.Equal("Server=db;Pwd=p", args[2]);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", args[3]);
    }

    [Fact]
    public void DbContextScaffold_All_Toggles_Round_Trip()
    {
        var conn = new Secret("C", "x");
        var args = EFCore.DbContextScaffold(FakeTool(), s => s
            .SetConnection(conn)
            .SetProvider("P")
            .SetDataAnnotations()
            .SetForce()
            .SetNoOnConfiguring()
            .SetNoPluralize()
            .SetUseDatabaseNames()).Arguments;
        Assert.Contains("--data-annotations", args);
        Assert.Contains("--force", args);
        Assert.Contains("--no-onconfiguring", args);
        Assert.Contains("--no-pluralize", args);
        Assert.Contains("--use-database-names", args);
    }

    [Fact]
    public void DbContextScaffold_Schemas_And_Tables_Repeat_Their_Flags()
    {
        var conn = new Secret("C", "x");
        var args = EFCore.DbContextScaffold(FakeTool(), s => s
            .SetConnection(conn)
            .SetProvider("P")
            .AddSchema("dbo")
            .AddSchema("audit")
            .AddTable("Users")
            .AddTable("Orders")
            .AddTable("audit.Events")).Arguments;
        Assert.Equal(2, args.Count(a => a == "--schema"));
        Assert.Equal(3, args.Count(a => a == "--table"));
        // Order preserved
        var schemaIdx = IndexOf(args, "--schema");
        Assert.Equal("dbo", args[schemaIdx + 1]);
        Assert.Equal("audit", args[IndexOf(args, "--schema", schemaIdx + 1) + 1]);
    }

    [Fact]
    public void DbContextScaffold_Connection_Is_Always_Registered_As_Secret()
    {
        var conn = new Secret("DbConn", "Server=prod;Pwd=topsecret");
        var plan = EFCore.DbContextScaffold(FakeTool(), s => s.SetConnection(conn).SetProvider("P"));
        Assert.Same(conn, Assert.Single(plan.Secrets));
    }

    [Fact]
    public void DbContextScaffold_Context_Naming_Round_Trip()
    {
        var conn = new Secret("C", "x");
        var args = EFCore.DbContextScaffold(FakeTool(), s => s
            .SetConnection(conn)
            .SetProvider("P")
            .SetContext("MyCtx")
            .SetContextDir("Data")
            .SetContextNamespace("MyApp.Data")
            .SetOutputDir("Entities")
            .SetNamespace("MyApp.Entities")).Arguments;
        Assert.Equal("MyCtx", args[IndexOf(args, "--context") + 1]);
        Assert.Equal("Data", args[IndexOf(args, "--context-dir") + 1]);
        Assert.Equal("MyApp.Data", args[IndexOf(args, "--context-namespace") + 1]);
        Assert.Equal("Entities", args[IndexOf(args, "--output-dir") + 1]);
        Assert.Equal("MyApp.Entities", args[IndexOf(args, "--namespace") + 1]);
    }

    // ================================================================
    // migrations add (REQUIRED: name)
    // ================================================================

    [Fact]
    public void MigrationsAdd_Throws_When_Name_Missing()
        => Assert.Throws<InvalidOperationException>(() => EFCore.MigrationsAdd(FakeTool(), _ => { }));

    [Fact]
    public void MigrationsAdd_Throws_When_Name_Empty()
        => Assert.Throws<InvalidOperationException>(() => EFCore.MigrationsAdd(FakeTool(), s => s.SetName("")));

    [Fact]
    public void MigrationsAdd_Name_Is_Positional_After_Verb()
    {
        var args = EFCore.MigrationsAdd(FakeTool(), s => s.SetName("AddUserTable")).Arguments;
        Assert.Equal("migrations", args[0]);
        Assert.Equal("add", args[1]);
        Assert.Equal("AddUserTable", args[2]);
    }

    [Fact]
    public void MigrationsAdd_Output_Namespace_Context_Round_Trip()
    {
        var args = EFCore.MigrationsAdd(FakeTool(), s => s
            .SetName("M1")
            .SetOutputDir("Migrations")
            .SetNamespace("MyApp.Mig")
            .SetContext("Ctx")).Arguments;
        Assert.Equal("Migrations", args[IndexOf(args, "--output-dir") + 1]);
        Assert.Equal("MyApp.Mig", args[IndexOf(args, "--namespace") + 1]);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
    }

    [Theory]
    [InlineData("AddUserEmail")]
    [InlineData("MigrationWith_Underscore")]
    [InlineData("UnicodeMigration_日本語")]
    public void MigrationsAdd_Name_Round_Trips_Various_Identifiers(string name)
    {
        var args = EFCore.MigrationsAdd(FakeTool(), s => s.SetName(name)).Arguments;
        Assert.Equal(name, args[2]);
    }

    // ================================================================
    // migrations remove / list
    // ================================================================

    [Fact]
    public void MigrationsRemove_Force_And_Context_Round_Trip()
    {
        var args = EFCore.MigrationsRemove(FakeTool(), s => s.SetForce().SetContext("Ctx")).Arguments;
        Assert.Equal(new[] { "migrations", "remove" }, args.Take(2));
        Assert.Contains("--force", args);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
    }

    [Fact]
    public void MigrationsList_NoConnect_Connection_Round_Trip()
    {
        var conn = new Secret("C", "abc");
        var plan = EFCore.MigrationsList(FakeTool(), s => s.SetConnection(conn).SetNoConnect().SetContext("Ctx"));
        var args = plan.Arguments;
        Assert.Equal(new[] { "migrations", "list" }, args.Take(2));
        Assert.Equal("abc", args[IndexOf(args, "--connection") + 1]);
        Assert.Contains("--no-connect", args);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
        Assert.Same(conn, Assert.Single(plan.Secrets));
    }

    // ================================================================
    // migrations script
    // ================================================================

    [Fact]
    public void MigrationsScript_Bare_Has_Only_Verb_Tokens()
    {
        var args = EFCore.MigrationsScript(FakeTool()).Arguments;
        Assert.Equal(new[] { "migrations", "script" }, args.ToArray());
    }

    [Fact]
    public void MigrationsScript_From_Only_Is_Positional()
    {
        var args = EFCore.MigrationsScript(FakeTool(), s => s.SetFrom("0")).Arguments;
        Assert.Equal("migrations", args[0]);
        Assert.Equal("script", args[1]);
        Assert.Equal("0", args[2]);
    }

    [Fact]
    public void MigrationsScript_From_And_To_Both_Positional_In_Order()
    {
        var args = EFCore.MigrationsScript(FakeTool(), s => s.SetFrom("M1").SetTo("M5")).Arguments;
        Assert.Equal("M1", args[2]);
        Assert.Equal("M5", args[3]);
    }

    [Fact]
    public void MigrationsScript_To_Without_From_Throws()
    {
        // EF's positional CLI shape doesn't allow "script <to>" — you'd
        // need to supply both. Surface this as a validation error rather
        // than emit a malformed command.
        Assert.Throws<InvalidOperationException>(() =>
            EFCore.MigrationsScript(FakeTool(), s => s.SetTo("M5")));
    }

    [Fact]
    public void MigrationsScript_Idempotent_NoTransactions_Output_Round_Trip()
    {
        var args = EFCore.MigrationsScript(FakeTool(), s => s
            .SetIdempotent()
            .SetNoTransactions()
            .SetOutput("upgrade.sql")).Arguments;
        Assert.Contains("--idempotent", args);
        Assert.Contains("--no-transactions", args);
        Assert.Equal("upgrade.sql", args[IndexOf(args, "--output") + 1]);
    }

    // ================================================================
    // migrations bundle
    // ================================================================

    [Fact]
    public void MigrationsBundle_Full_Surface_Round_Trip()
    {
        var args = EFCore.MigrationsBundle(FakeTool(), s => s
            .SetOutput("migrate")
            .SetTargetRuntime("linux-x64")
            .SetSelfContained()
            .SetForce()
            .SetContext("Ctx")).Arguments;
        Assert.Equal(new[] { "migrations", "bundle" }, args.Take(2));
        Assert.Equal("migrate", args[IndexOf(args, "--output") + 1]);
        Assert.Equal("linux-x64", args[IndexOf(args, "--target-runtime") + 1]);
        Assert.Contains("--self-contained", args);
        Assert.Contains("--force", args);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
    }

    // ================================================================
    // migrations has-pending-model-changes
    // ================================================================

    [Fact]
    public void MigrationsHasPendingModelChanges_Verb_Tokens_Then_Optional_Context()
    {
        var args = EFCore.MigrationsHasPendingModelChanges(FakeTool(), s => s.SetContext("Ctx")).Arguments;
        Assert.Equal("migrations", args[0]);
        Assert.Equal("has-pending-model-changes", args[1]);
        Assert.Equal("Ctx", args[IndexOf(args, "--context") + 1]);
    }

    // ================================================================
    // Common base settings — every verb inherits these
    // ================================================================

    [Fact]
    public void Common_Project_StartupProject_Round_Trip()
    {
        var args = EFCore.MigrationsList(FakeTool(), s => s
            .SetProject("./src/Data.csproj")
            .SetStartupProject("./src/Api.csproj")).Arguments;
        Assert.Equal("./src/Data.csproj", args[IndexOf(args, "--project") + 1]);
        Assert.Equal("./src/Api.csproj", args[IndexOf(args, "--startup-project") + 1]);
    }

    [Fact]
    public void Common_Framework_Configuration_Runtime_Round_Trip()
    {
        var args = EFCore.DatabaseUpdate(FakeTool(), s => s
            .SetFramework("net10.0")
            .SetConfiguration("Release")
            .SetRuntime("linux-x64")).Arguments;
        Assert.Equal("net10.0", args[IndexOf(args, "--framework") + 1]);
        Assert.Equal("Release", args[IndexOf(args, "--configuration") + 1]);
        Assert.Equal("linux-x64", args[IndexOf(args, "--runtime") + 1]);
    }

    [Fact]
    public void Common_NoBuild_Emits_Flag()
    {
        var args = EFCore.DatabaseUpdate(FakeTool(), s => s.SetNoBuild()).Arguments;
        Assert.Contains("--no-build", args);
    }

    [Fact]
    public void Common_MsBuildProjectExtensionsPath_Round_Trips()
    {
        var args = EFCore.MigrationsAdd(FakeTool(), s => s.SetName("M1").SetMsBuildProjectExtensionsPath("/tmp/obj")).Arguments;
        Assert.Equal("/tmp/obj", args[IndexOf(args, "--msbuildprojectextensionspath") + 1]);
    }

    [Fact]
    public void Common_ProcessWorkingDirectory_Sets_The_Spawned_Process_Cwd()
    {
        // EF's CLI has no --working-dir flag (despite what the tooling
        // docs occasionally imply). The OS process working directory is
        // the only knob — set it via SetProcessWorkingDirectory.
        var plan = EFCore.MigrationsList(FakeTool(), s => s
            .SetProcessWorkingDirectory("/process/cwd"));
        Assert.Equal("/process/cwd", plan.WorkingDirectory);
        Assert.DoesNotContain("--working-dir", plan.Arguments);
    }

    [Fact]
    public void Common_Verbose_NoColor_PrefixOutput_Round_Trip()
    {
        // --json is per-verb (some verbs reject it). DbContextList
        // happens to support it, so it round-trips here.
        var args = EFCore.DbContextList(FakeTool(), s => s
            .SetVerbose()
            .SetNoColor()
            .SetPrefixOutput()
            .SetJson()).Arguments;
        Assert.Contains("--verbose", args);
        Assert.Contains("--no-color", args);
        Assert.Contains("--prefix-output", args);
        Assert.Contains("--json", args);
    }

    [Fact]
    public void Json_Is_Per_Verb_Not_On_Common_Base()
    {
        // Surface-policing: --json is only accepted on a subset of EF
        // verbs. The base class must NOT carry it; otherwise verbs that
        // reject --json would silently emit a flag the CLI errors on.
        var setters = typeof(EFCoreSettingsBase)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Set"))
            .Select(m => m.Name)
            .ToHashSet();
        Assert.DoesNotContain("SetJson", setters);
    }

    [Fact]
    public void Verbs_Without_Json_Have_No_SetJson()
    {
        // EF 10 does NOT accept --json on these verbs. The wrapper
        // mirrors the real CLI surface.
        var withoutJson = new[]
        {
            typeof(EFCoreDatabaseUpdateSettings),
            typeof(EFCoreDatabaseDropSettings),
            typeof(EFCoreMigrationsScriptSettings),
            typeof(EFCoreMigrationsBundleSettings),
            typeof(EFCoreMigrationsHasPendingModelChangesSettings),
            typeof(EFCoreDbContextScriptSettings),
            typeof(EFCoreDbContextOptimizeSettings),
        };
        foreach (var t in withoutJson)
        {
            var setters = t.GetMethods().Where(m => m.Name == "SetJson").ToList();
            Assert.Empty(setters);
        }
    }

    [Fact]
    public void Verbs_With_Json_Each_Carry_Their_Own_SetJson()
    {
        // Each verb that supports --json declares the setter directly;
        // it is NOT inherited from the base.
        var withJson = new[]
        {
            typeof(EFCoreMigrationsAddSettings),
            typeof(EFCoreMigrationsRemoveSettings),
            typeof(EFCoreMigrationsListSettings),
            typeof(EFCoreDbContextInfoSettings),
            typeof(EFCoreDbContextListSettings),
            typeof(EFCoreDbContextScaffoldSettings),
        };
        foreach (var t in withJson)
        {
            var setter = t.GetMethod("SetJson");
            Assert.NotNull(setter);
            Assert.Equal(t, setter!.DeclaringType);
        }
    }

    [Fact]
    public void DbContextOptimize_NoScaffold_Round_Trips()
    {
        var args = EFCore.DbContextOptimize(FakeTool(), s => s.SetNoScaffold()).Arguments;
        Assert.Contains("--no-scaffold", args);
    }

    [Fact]
    public void Common_SetVerbose_False_Removes_Flag()
    {
        var args = EFCore.DbContextList(FakeTool(), s => s.SetVerbose().SetVerbose(false)).Arguments;
        Assert.DoesNotContain("--verbose", args);
    }

    [Fact]
    public void Common_EnvironmentVariables_Pass_Through_To_Plan()
    {
        var plan = EFCore.MigrationsList(FakeTool(), s => s
            .SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production")
            .SetEnvironmentVariable("FOO", "BAR"));
        Assert.Equal("Production", plan.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("BAR", plan.Environment["FOO"]);
    }

    [Fact]
    public void Common_Bare_Verb_Has_No_Common_Flags_Emitted()
    {
        var args = EFCore.DbContextList(FakeTool()).Arguments;
        Assert.DoesNotContain("--project", args);
        Assert.DoesNotContain("--startup-project", args);
        Assert.DoesNotContain("--framework", args);
        Assert.DoesNotContain("--configuration", args);
        Assert.DoesNotContain("--runtime", args);
        Assert.DoesNotContain("--no-build", args);
        Assert.DoesNotContain("--verbose", args);
        Assert.DoesNotContain("--json", args);
    }

    [Fact]
    public void Common_Verb_Tokens_Always_Come_Before_Common_Flags()
    {
        // Regression guard — verb words ("migrations", "add", <name>) must
        // be position-stable. EF's CLI parser treats anything after the
        // verb tokens as flags, so emitting --project before "migrations"
        // would silently fail (or worse, succeed with the wrong shape).
        var args = EFCore.MigrationsAdd(FakeTool(), s => s
            .SetName("AddX")
            .SetProject("/proj.csproj")
            .SetStartupProject("/startup.csproj")
            .SetNoBuild()).Arguments;
        Assert.Equal("migrations", args[0]);
        Assert.Equal("add", args[1]);
        Assert.Equal("AddX", args[2]);
        Assert.True(IndexOf(args, "--project") > 2);
        Assert.True(IndexOf(args, "--startup-project") > 2);
    }

    // ================================================================
    // Process working directory precedence: settings override > tool default
    // ================================================================

    [Fact]
    public void ProcessWorkingDirectory_Settings_Wins_Over_Tool()
    {
        var tool = new Tool(AbsolutePath.Create("/fake/dotnet-ef"), workingDirectory: "/from-tool");
        var plan = EFCore.MigrationsList(tool, s => s.SetProcessWorkingDirectory("/from-settings"));
        Assert.Equal("/from-settings", plan.WorkingDirectory);
    }

    [Fact]
    public void ProcessWorkingDirectory_Falls_Back_To_Tool_When_Settings_Null()
    {
        var tool = new Tool(AbsolutePath.Create("/fake/dotnet-ef"), workingDirectory: "/from-tool");
        var plan = EFCore.MigrationsList(tool);
        Assert.Equal("/from-tool", plan.WorkingDirectory);
    }

    // ================================================================
    // Boundary: large argument counts shouldn't degrade ordering
    // ================================================================

    [Fact]
    public void DbContextScaffold_With_Many_Schemas_Tables_Preserves_Order()
    {
        var f = Faker();
        var schemas = Enumerable.Range(0, 50).Select(_ => f.Database.Engine().Replace(' ', '_')).ToArray();
        var tables = Enumerable.Range(0, 200).Select(_ => $"{f.Random.AlphaNumeric(8)}.{f.Random.AlphaNumeric(12)}").ToArray();

        var conn = new Secret("C", "x");
        var configurer = (EFCoreDbContextScaffoldSettings s) =>
        {
            s.SetConnection(conn).SetProvider("P");
            foreach (var sc in schemas) s.AddSchema(sc);
            foreach (var t in tables) s.AddTable(t);
        };
        var args = EFCore.DbContextScaffold(FakeTool(), configurer).Arguments;

        // Every schema and table appears exactly once, in the order added.
        var schemaArgs = args.Select((a, i) => (a, i))
                             .Where(x => x.a == "--schema")
                             .Select(x => args[x.i + 1])
                             .ToArray();
        var tableArgs = args.Select((a, i) => (a, i))
                            .Where(x => x.a == "--table")
                            .Select(x => args[x.i + 1])
                            .ToArray();
        Assert.Equal(schemas, schemaArgs);
        Assert.Equal(tables, tableArgs);
    }

    // ================================================================
    // Secret redaction integration — Secret.ToString never leaks the value
    // ================================================================

    [Fact]
    public void Connection_Secret_Tostring_Never_Leaks_Value()
    {
        var conn = new Secret("Conn", "Server=prod;Pwd=hunter2");
        var plan = EFCore.DatabaseUpdate(FakeTool(), s => s.SetConnection(conn));
        // The secret object's ToString is what loggers see by default.
        Assert.Equal("<Secret:Conn>", conn.ToString());
        // The plan still has the real value in its arguments — that's
        // the runner's job to redact via the Secrets list. Just confirm
        // the registration is present.
        Assert.Same(conn, plan.Secrets[0]);
    }
}
