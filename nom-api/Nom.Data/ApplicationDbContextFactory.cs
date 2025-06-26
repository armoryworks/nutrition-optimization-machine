// Nom.Data/ApplicationDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Nom.Data
{
    /// <summary>
    /// A factory for creating instances of <see cref="ApplicationDbContext"/> at design time.
    /// This is necessary for Entity Framework Core CLI tools (like 'dotnet ef migrations add')
    /// to be able to instantiate the DbContext when it has a constructor that requires DbContextOptions.
    /// </summary>
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // Build configuration to read appsettings.json for the connection string
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Set base path to the directory where the app is running
                                                              // Add appsettings.json
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                // Add appsettings.Development.json if in development environment
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                // Optionally, add environment variables, etc.
                .Build();

            var connectionString = configuration.GetConnectionString("NomConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                // This will be caught by the EF Core tools and reported as an error
                throw new InvalidOperationException("Connection string 'NomConnection' not found.");
            }

            // Configure DbContextOptions for PostgreSQL
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString,
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)); // Specify the assembly where migrations are located

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
