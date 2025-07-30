using Microsoft.Extensions.Configuration;
using System;

namespace Nom.Import
{
    public class ConnectionStringTest
    {
        public static void TestConnectionString()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.enhanced.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            var connectionString = configuration.GetConnectionString("NomConnection");
            
            Console.WriteLine("=== Connection String Test ===");
            Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Not Set"}");
            Console.WriteLine($"Connection String: {connectionString}");
            Console.WriteLine("=============================");
            
            if (connectionString?.Contains("NomUser") == true)
            {
                Console.WriteLine("✅ SUCCESS: Using correct connection string with NomUser");
            }
            else if (connectionString?.Contains("postgres") == true)
            {
                Console.WriteLine("❌ ERROR: Using placeholder connection string with postgres");
            }
            else
            {
                Console.WriteLine("❌ ERROR: No connection string found");
            }
        }
    }
} 