using Xunit;

namespace Tamp.EFCore.V10.Tests;

/// <summary>
/// Object-init overload coverage for the EF Core wrapper (TAM-161 satellite fanout).
/// The fluent (<c>Action&lt;TSettings&gt;</c>) form and the object-init form
/// (<c>TSettings</c>) must produce byte-equal CommandPlans, and every public
/// verb must expose the object-init overload.
/// </summary>
public sealed class ObjectInitTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/dotnet-ef"));

    // ================================================================
    // Round-trip: fluent vs object-init produce identical Arguments
    // ================================================================

    [Fact]
    public void MigrationsAdd_ObjectInit_Emits_Identical_Plan_To_Fluent()
    {
        var tool = FakeTool();

        var fluent = EFCore.MigrationsAdd(tool, s => s
            .SetName("InitialCreate")
            .SetProject("./src/App/App.csproj")
            .SetStartupProject("./src/App/App.csproj")
            .SetOutputDir("Migrations")
            .SetNamespace("App.Data.Migrations")
            .SetContext("AppDbContext")
            .SetConfiguration("Release")
            .SetNoBuild()
            .SetVerbose()
            .SetJson());

        var objectInit = EFCore.MigrationsAdd(tool, new EFCoreMigrationsAddSettings
        {
            Name = "InitialCreate",
            Project = "./src/App/App.csproj",
            StartupProject = "./src/App/App.csproj",
            OutputDir = "Migrations",
            Namespace = "App.Data.Migrations",
            Context = "AppDbContext",
            Configuration = "Release",
            NoBuild = true,
            Verbosity = EFCoreVerbosity.Verbose,
            Json = true,
        });

        Assert.Equal(fluent.Executable, objectInit.Executable);
        Assert.Equal(fluent.Arguments, objectInit.Arguments);
    }

    [Fact]
    public void DatabaseUpdate_ObjectInit_Round_Trips_Secrets_And_Args()
    {
        var tool = FakeTool();
        var conn = new Secret("ConnectionString", "Server=db;User=sa;Password=hunter2");

        var fluent = EFCore.DatabaseUpdate(tool, s => s
            .SetTargetMigration("0")
            .SetConnection(conn)
            .SetContext("AppDbContext"));

        var objectInit = EFCore.DatabaseUpdate(tool, new EFCoreDatabaseUpdateSettings
        {
            TargetMigration = "0",
            Connection = conn,
            Context = "AppDbContext",
        });

        Assert.Equal(fluent.Arguments, objectInit.Arguments);
        Assert.Equal(fluent.Secrets.Count, objectInit.Secrets.Count);
        Assert.Same(conn, objectInit.Secrets[0]);
    }

    // ================================================================
    // Smoke: every object-init overload is callable and returns a plan
    // ================================================================

    [Fact]
    public void Every_Verb_Has_ObjectInit_Overload_Returning_Non_Null_Plan()
    {
        var tool = FakeTool();

        Assert.NotNull(EFCore.DatabaseUpdate(tool, new EFCoreDatabaseUpdateSettings()));
        Assert.NotNull(EFCore.DatabaseDrop(tool, new EFCoreDatabaseDropSettings()));
        Assert.NotNull(EFCore.DbContextInfo(tool, new EFCoreDbContextInfoSettings()));
        Assert.NotNull(EFCore.DbContextList(tool, new EFCoreDbContextListSettings()));
        Assert.NotNull(EFCore.DbContextOptimize(tool, new EFCoreDbContextOptimizeSettings()));
        Assert.NotNull(EFCore.DbContextScript(tool, new EFCoreDbContextScriptSettings()));
        Assert.NotNull(EFCore.DbContextScaffold(tool, new EFCoreDbContextScaffoldSettings
        {
            Connection = new Secret("ConnectionString", "Server=db;User=sa;Password=hunter2"),
            Provider = "Microsoft.EntityFrameworkCore.SqlServer",
        }));
        Assert.NotNull(EFCore.MigrationsAdd(tool, new EFCoreMigrationsAddSettings { Name = "M1" }));
        Assert.NotNull(EFCore.MigrationsRemove(tool, new EFCoreMigrationsRemoveSettings()));
        Assert.NotNull(EFCore.MigrationsList(tool, new EFCoreMigrationsListSettings()));
        Assert.NotNull(EFCore.MigrationsScript(tool, new EFCoreMigrationsScriptSettings()));
        Assert.NotNull(EFCore.MigrationsBundle(tool, new EFCoreMigrationsBundleSettings()));
        Assert.NotNull(EFCore.MigrationsHasPendingModelChanges(tool, new EFCoreMigrationsHasPendingModelChangesSettings()));
    }

    // ================================================================
    // Guard rails: null tool / null settings still throw
    // ================================================================

    [Fact]
    public void ObjectInit_Throws_On_Null_Tool()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EFCore.MigrationsAdd(null!, new EFCoreMigrationsAddSettings { Name = "M1" }));
    }

    [Fact]
    public void ObjectInit_Throws_On_Null_Settings()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EFCore.MigrationsAdd(FakeTool(), (EFCoreMigrationsAddSettings)null!));
    }
}
