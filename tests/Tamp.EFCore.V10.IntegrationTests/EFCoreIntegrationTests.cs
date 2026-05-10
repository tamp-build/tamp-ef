using System.IO;
using Microsoft.Data.Sqlite;
using Tamp;
using Tamp.EFCore.V10;
using Xunit;
using Xunit.Abstractions;

namespace Tamp.EFCore.V10.IntegrationTests;

/// <summary>
/// End-to-end exercises of the wrapper against a real EF Core 10
/// sample app. Each test stages an isolated copy of the sample so
/// migrations / database files don't bleed across runs.
/// </summary>
public sealed class EFCoreIntegrationTests
{
    private readonly ITestOutputHelper _output;
    public EFCoreIntegrationTests(ITestOutputHelper output) => _output = output;

    private CaptureResult Run(CommandPlan plan)
    {
        _output.WriteLine($"$ {plan.Executable} {string.Join(' ', plan.Arguments)}");
        var result = ProcessRunner.Capture(plan);
        foreach (var line in result.Lines)
            _output.WriteLine($"  [{line.Type}] {line.Text}");
        _output.WriteLine($"  → exit {result.ExitCode}");
        return result;
    }

    [Fact]
    public void MigrationsAdd_Creates_Migration_Files_In_Project()
    {
        using var sample = SampleApp.Stage();

        var plan = EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"));
        var result = Run(plan);

        Assert.Equal(0, result.ExitCode);
        var migrationsDir = sample.Root / "Migrations";
        Assert.True(migrationsDir.DirectoryExists(), "Migrations directory should exist after `migrations add`.");
        var files = migrationsDir.EnumerateFiles().Select(f => f.Name).ToList();
        Assert.Contains(files, n => n.EndsWith("_Initial.cs"));
        Assert.Contains(files, n => n.EndsWith("_Initial.Designer.cs"));
        Assert.Contains(files, n => n == "AppDbContextModelSnapshot.cs");
    }

    [Fact]
    public void DatabaseUpdate_Applies_Migration_And_Creates_SQLite_Table()
    {
        using var sample = SampleApp.Stage();

        // 1. Add the migration.
        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();

        // 2. Apply it. Override connection so the SQLite file lands at a known path.
        var conn = new Secret("ConnectionString", sample.ConnectionString);
        var update = Run(EFCore.DatabaseUpdate(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetConnection(conn)));
        Assert.Equal(0, update.ExitCode);

        // 3. Verify the SQLite file has the Customers table.
        Assert.True(sample.ConnectionDbFile.FileExists(), "SQLite file should exist after database update.");
        using var connection = new SqliteConnection(sample.ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Customers'";
        var name = cmd.ExecuteScalar() as string;
        Assert.Equal("Customers", name);
    }

    [Fact]
    public void MigrationsList_Shows_Added_Migration()
    {
        using var sample = SampleApp.Stage();

        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();

        var result = Run(EFCore.MigrationsList(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetNoConnect()));
        Assert.Equal(0, result.ExitCode);
        // The migration name appears in stdout in some form. dotnet-ef
        // formats it as "<timestamp>_Initial" typically.
        Assert.Contains(result.StdoutLines, l => l.Contains("_Initial", StringComparison.Ordinal));
    }

    [Fact]
    public void MigrationsScript_Emits_Create_Customers_Table_SQL()
    {
        using var sample = SampleApp.Stage();

        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();

        var output = sample.Root / "schema.sql";
        // SQLite doesn't support EF's --idempotent script generation
        // (the SQL dialect lacks IF NOT EXISTS in the relevant places).
        // Skip --idempotent for the SQLite-backed sample; use it on SQL
        // Server / PostgreSQL projects.
        var result = Run(EFCore.MigrationsScript(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetOutput(output.Value)));
        Assert.Equal(0, result.ExitCode);
        Assert.True(output.FileExists(), "SQL output file should exist.");
        var sql = File.ReadAllText(output.Value);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Customers", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DbContextInfo_Reports_Provider_And_Database_Name()
    {
        using var sample = SampleApp.Stage();

        var result = Run(EFCore.DbContextInfo(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetNoBuild()));
        Assert.Equal(0, result.ExitCode);
        var stdout = result.StdoutText;
        Assert.Contains("Sqlite", stdout);
        Assert.Contains("AppDbContext", stdout);
    }

    [Fact]
    public void DbContextList_Returns_Json_Including_AppDbContext()
    {
        using var sample = SampleApp.Stage();

        var result = Run(EFCore.DbContextList(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetJson()));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.StdoutLines, l => l.Contains("AppDbContext", StringComparison.Ordinal));
    }

    [Fact]
    public void MigrationsHasPendingModelChanges_Exits_Zero_When_Model_Matches_Migrations()
    {
        using var sample = SampleApp.Stage();

        // Add the migration, so the latest snapshot matches the model.
        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();

        // Need to rebuild after generating migration sources.
        Run(new CommandPlan
        {
            Executable = "dotnet",
            Arguments = new[] { "build", sample.ProjectFile.Value, "-nologo", "-v:q" },
        }).ThrowOnFailure();

        var result = Run(EFCore.MigrationsHasPendingModelChanges(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetNoBuild()));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DatabaseDrop_With_Force_Removes_Existing_SQLite_File()
    {
        using var sample = SampleApp.Stage();

        // Set up a database first. database update accepts --connection;
        // drop does not, so we rely on the OnConfiguring default for drop.
        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();
        var conn = new Secret("Conn", sample.ConnectionString);
        Run(EFCore.DatabaseUpdate(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetConnection(conn))).ThrowOnFailure();
        Assert.True(sample.ConnectionDbFile.FileExists(), "Precondition: SQLite file exists before drop.");

        // database drop uses the connection from OnConfiguring (which
        // points to "Data Source=app.db" relative to the working dir).
        // Set ProcessWorkingDirectory so the relative SQLite path
        // resolves to the same file we just created.
        File.Move(sample.ConnectionDbFile.Value, Path.Combine(sample.Root.Value, "app.db"));

        var result = Run(EFCore.DatabaseDrop(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetForce()
            .SetProcessWorkingDirectory(sample.Root.Value)));
        Assert.Equal(0, result.ExitCode);
        Assert.False((sample.Root / "app.db").FileExists(), "SQLite file should be gone after drop.");
    }

    [Fact]
    public void DatabaseDrop_DryRun_Reports_Without_Deleting()
    {
        using var sample = SampleApp.Stage();

        Run(EFCore.MigrationsAdd(sample.Ef, s => s
            .SetName("Initial")
            .SetProject(sample.ProjectFile)
            .SetNoBuild()
            .SetOutputDir("Migrations"))).ThrowOnFailure();
        var conn = new Secret("Conn", sample.ConnectionString);
        Run(EFCore.DatabaseUpdate(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetConnection(conn))).ThrowOnFailure();
        File.Move(sample.ConnectionDbFile.Value, Path.Combine(sample.Root.Value, "app.db"));

        var result = Run(EFCore.DatabaseDrop(sample.Ef, s => s
            .SetProject(sample.ProjectFile)
            .SetForce()
            .SetDryRun()
            .SetProcessWorkingDirectory(sample.Root.Value)));
        Assert.Equal(0, result.ExitCode);
        Assert.True((sample.Root / "app.db").FileExists(), "Dry-run must NOT delete the database file.");
    }
}
