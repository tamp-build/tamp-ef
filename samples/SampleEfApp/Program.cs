using Microsoft.EntityFrameworkCore;

namespace SampleEfApp;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // The integration tests pass --connection at runtime to override
        // this default. The default is here so `dotnet ef` can resolve
        // the design-time DbContext without further configuration.
        if (!options.IsConfigured)
            options.UseSqlite("Data Source=app.db");
    }
}

internal class Program
{
    private static int Main()
    {
        // The sample only exists to give dotnet-ef something to load.
        // It never runs end-to-end on its own.
        return 0;
    }
}
