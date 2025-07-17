using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql; // For PostgreSQL interaction
using CsvHelper; // For reading CSV files
using CsvHelper.Configuration; // For CSV configuration
using System.Globalization;
using System.Collections.Concurrent; // For ConcurrentBag
using System.Text.RegularExpressions; // For SanitizeTableName



public class Program
{
    // Define the directory where your CSV files are located
    private const string BaseDataPath = "/home/dhokanson/Dev/ImportSource/";
    // Number of rows to sample from each CSV to infer column types
    private const int TypeInferenceSampleRows = 100;

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting generic CSV import application...");

        // Build configuration from appsettings.Development.json
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true);
        IConfiguration config = builder.Build();

        string connectionString = config.GetConnectionString("TmpConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("Error: 'TmpConnection' connection string not found in appsettings.Development.json.");
            Console.WriteLine("Please ensure the file exists and contains the connection string.");
            return;
        }

        // Extract database name from the connection string
        var connStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        string targetDatabaseName = connStringBuilder.Database;
        // Temporarily connect to 'postgres' database to create the target database if it doesn't exist
        connStringBuilder.Database = "postgres";
        string masterConnectionString = connStringBuilder.ToString();

        try
        {
            await EnsureDatabaseExistsAsync(masterConnectionString, targetDatabaseName);
            Console.WriteLine($"Database '{targetDatabaseName}' is ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error ensuring database exists: {ex.Message}");
            return;
        }

        Console.WriteLine($"Scanning for CSV files in: {BaseDataPath}");

        if (!Directory.Exists(BaseDataPath))
        {
            Console.WriteLine($"Error: Directory not found at {BaseDataPath}. Please ensure the path is correct.");
            return;
        }

        var csvFiles = Directory.GetFiles(BaseDataPath, "*.csv", SearchOption.TopDirectoryOnly).ToList();

        if (!csvFiles.Any())
        {
            Console.WriteLine("No CSV files found in the specified directory. Exiting.");
            return;
        }

        Console.WriteLine($"Found {csvFiles.Count} CSV files. Starting parallel processing...");

        // Use Parallel.ForEachAsync to process each CSV file concurrently
        // MaxDegreeOfParallelism is set to the number of CPU cores for optimal utilization
        await Parallel.ForEachAsync(csvFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (filePath, token) =>
        {
            await ProcessCsvFileAsync(filePath, connectionString);
        });

        Console.WriteLine("\nAll CSV files processed. Import complete.");
    }

    /// <summary>
    /// Ensures the target PostgreSQL database exists. If not, it creates it.
    /// </summary>
    /// <param name="masterConnectionString">Connection string to a master database (e.g., 'postgres').</param>
    /// <param name="databaseName">The name of the database to ensure exists.</param>
    private static async Task EnsureDatabaseExistsAsync(string masterConnectionString, string databaseName)
    {
        await using var conn = new NpgsqlConnection(masterConnectionString);
        await conn.OpenAsync();

        // Check if the database exists
        await using (var cmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @dbName", conn))
        {
            cmd.Parameters.AddWithValue("dbName", databaseName);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result.Equals(1))
            {
                Console.WriteLine($"Database '{databaseName}' already exists.");
                return;
            }
        }

        // Create the database if it does not exist
        Console.WriteLine($"Database '{databaseName}' does not exist. Creating...");
        // IMPORTANT: Cannot use parameters with CREATE DATABASE. Sanitize name carefully.
        string safeDbName = Regex.Replace(databaseName, "[^a-zA-Z0-9_]", "", RegexOptions.Compiled);
        if (string.IsNullOrWhiteSpace(safeDbName))
        {
            throw new ArgumentException("Database name cannot be empty or contain only invalid characters after sanitization.");
        }
        await using (var cmd = new NpgsqlCommand($"CREATE DATABASE \"{safeDbName}\"", conn))
        {
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Database '{databaseName}' created successfully.");
        }
    }

    /// <summary>
    /// Processes a single CSV file: infers schema, creates table, and bulk inserts data.
    /// </summary>
    /// <param name="filePath">The full path to the CSV file.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task ProcessCsvFileAsync(string filePath, string connectionString)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string tableName = SanitizeTableName(fileName);

        Console.WriteLine($"Processing file: {fileName}.csv -> Table: \"{tableName}\"");

        try
        {
            // Infer column names and types
            var columnDefinitions = InferColumnTypes(filePath);
            if (!columnDefinitions.Any())
            {
                Console.WriteLine($"Warning: No columns inferred for {fileName}.csv. Skipping table creation and import.");
                return;
            }

            // Create table
            await CreateTableAsync(connectionString, tableName, columnDefinitions);

            // Bulk insert data
            await BulkInsertDataAsync(connectionString, filePath, tableName, columnDefinitions);

            Console.WriteLine($"Successfully imported {fileName}.csv into table \"{tableName}\".");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {fileName}.csv: {ex.Message}");
            // Log the full exception details if needed for debugging
            // Console.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    /// Infers column names and their PostgreSQL data types by sampling the CSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <returns>A list of tuples containing sanitized column name and inferred SQL type.</returns>
    private static List<(string ColumnName, string SqlType)> InferColumnTypes(string filePath)
    {
        var columnTypes = new Dictionary<string, Type>(); // Tracks the "strongest" type found for each column
        var columnNames = new List<string>();

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => SanitizeColumnName(args.Header), // Sanitize headers for internal use
            MissingFieldFound = null, // Do not throw if a field is missing
            BadDataFound = null // Do not throw on bad data
        };

        using (var reader = new StreamReader(filePath))
        using (var csv = new CsvReader(reader, csvConfig))
        {
            csv.Read();
            csv.ReadHeader();
            foreach (var header in csv.HeaderRecord)
            {
                string sanitizedHeader = SanitizeColumnName(header);
                columnNames.Add(sanitizedHeader);
                columnTypes[sanitizedHeader] = typeof(string); // Default to string (TEXT in SQL)
            }

            int rowsRead = 0;
            while (csv.Read() && rowsRead < TypeInferenceSampleRows)
            {
                for (int i = 0; i < columnNames.Count; i++)
                {
                    string columnName = columnNames[i];
                    string rawValue = csv.GetField(i);

                    Type currentInferredType = GetPostgreSqlType(rawValue);
                    Type existingType = columnTypes[columnName];

                    // Promote type if new type is "stronger" (e.g., int -> double -> string)
                    columnTypes[columnName] = PromoteType(existingType, currentInferredType);
                }
                rowsRead++;
            }
        }

        // Convert inferred C# types to PostgreSQL SQL types
        var sqlColumnDefinitions = new List<(string ColumnName, string SqlType)>();
        foreach (var name in columnNames)
        {
            string sqlTypeName = "TEXT"; // Default fallback
            Type inferredType = columnTypes[name];

            if (inferredType == typeof(int)) sqlTypeName = "INTEGER";
            else if (inferredType == typeof(double)) sqlTypeName = "DOUBLE PRECISION";
            else if (inferredType == typeof(bool)) sqlTypeName = "BOOLEAN";
            else if (inferredType == typeof(DateTime)) sqlTypeName = "TIMESTAMP WITHOUT TIME ZONE";
            // else remains TEXT

            sqlColumnDefinitions.Add((name, sqlTypeName));
        }

        return sqlColumnDefinitions;
    }

    /// <summary>
    /// Determines the "strongest" type between two given types for schema inference.
    /// </summary>
    private static Type PromoteType(Type existingType, Type newType)
    {
        if (existingType == typeof(string)) return typeof(string); // String is the broadest
        if (newType == typeof(string)) return typeof(string);

        if (existingType == typeof(double)) return typeof(double); // Double is broader than int
        if (newType == typeof(double)) return typeof(double);

        if (existingType == typeof(DateTime)) return typeof(DateTime); // DateTime is specific
        if (newType == typeof(DateTime)) return typeof(DateTime);

        if (existingType == typeof(int) && newType == typeof(int)) return typeof(int);
        if (existingType == typeof(bool) && newType == typeof(bool)) return typeof(bool);

        // If types are incompatible (e.g., int and bool), promote to string
        if (existingType != newType) return typeof(string);

        return existingType; // Should be the same type now
    }

    /// <summary>
    /// Infers the PostgreSQL data type for a single string value.
    /// </summary>
    private static Type GetPostgreSqlType(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return typeof(string); // Treat empty/whitespace as string for now

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return typeof(int);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return typeof(double);
        if (bool.TryParse(value, out _)) return typeof(bool);
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return typeof(DateTime);

        return typeof(string); // Default to string if no specific type matches
    }

    /// <summary>
    /// Creates a table in the PostgreSQL database based on inferred column definitions.
    /// </summary>
    private static async Task CreateTableAsync(string connectionString, string tableName, List<(string ColumnName, string SqlType)> columnDefinitions)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var columnsSql = string.Join(", ", columnDefinitions.Select(col => $"\"{col.Item1}\" {col.Item2}"));
        var createTableSql = $"CREATE TABLE IF NOT EXISTS \"{tableName}\" ({columnsSql})";

        Console.WriteLine($"Attempting to create table: {tableName}");
        // Console.WriteLine($"SQL: {createTableSql}"); // Uncomment for debugging SQL

        await using var cmd = new NpgsqlCommand(createTableSql, conn);
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"Table \"{tableName}\" ensured to exist.");
    }

    /// <summary>
    /// Bulk inserts data from a CSV file into the specified PostgreSQL table.
    /// Uses Npgsql's binary import for maximum efficiency.
    /// </summary>
    private static async Task BulkInsertDataAsync(string connectionString, string filePath, string tableName, List<(string ColumnName, string SqlType)> columnDefinitions)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var columnNames = columnDefinitions.Select(c => $"\"{c.ColumnName}\"").ToList();
        // Corrected: Use FORMAT BINARY for BeginBinaryImport
        var copyCommand = $"COPY \"{tableName}\" ({string.Join(", ", columnNames)}) FROM STDIN (FORMAT BINARY)";

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => SanitizeColumnName(args.Header),
            MissingFieldFound = null,
            BadDataFound = null
        };

        Console.WriteLine($"Starting bulk import for table \"{tableName}\" using binary format...");

        using (var reader = new StreamReader(filePath))
        using (var csv = new CsvReader(reader, csvConfig))
        using (var importer = conn.BeginBinaryImport(copyCommand))
        {
            csv.Read(); // Read header row
            csv.ReadHeader(); // Process header

            int rowsImported = 0;
            while (csv.Read())
            {
                importer.StartRow();
                for (int i = 0; i < columnDefinitions.Count; i++)
                {
                    var (colName, sqlType) = columnDefinitions[i];
                    string rawValue = csv.GetField(i);

                    // Write DBNull.Value for empty strings for nullable types
                    if (string.IsNullOrEmpty(rawValue))
                    {
                        importer.Write(DBNull.Value);
                    }
                    else
                    {
                        // Write based on inferred type. Npgsql handles the binary serialization.
                        if (sqlType == "INTEGER")
                        {
                            importer.Write(int.Parse(rawValue, CultureInfo.InvariantCulture));
                        }
                        else if (sqlType == "DOUBLE PRECISION")
                        {
                            importer.Write(double.Parse(rawValue, CultureInfo.InvariantCulture));
                        }
                        else if (sqlType == "BOOLEAN")
                        {
                            importer.Write(bool.Parse(rawValue));
                        }
                        else if (sqlType == "TIMESTAMP WITHOUT TIME ZONE")
                        {
                            importer.Write(DateTime.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None));
                        }
                        else // TEXT or any other unhandled type
                        {
                            importer.Write(rawValue);
                        }
                    }
                }
                rowsImported++;
            }

            await importer.CompleteAsync();
            Console.WriteLine($"Bulk imported {rowsImported} rows into \"{tableName}\".");
        }
    }

    /// <summary>
    /// Sanitizes a string to be a valid PostgreSQL table name.
    /// Replaces invalid characters with underscores and ensures it starts with a letter or underscore.
    /// </summary>
    private static string SanitizeTableName(string name)
    {
        // Replace non-alphanumeric characters (except underscore) with underscore
        string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]+", "_");
        // Ensure it doesn't start with a number (PostgreSQL identifiers can't)
        if (char.IsDigit(sanitized.FirstOrDefault()))
        {
            sanitized = "_" + sanitized;
        }
        // Trim underscores from ends
        sanitized = sanitized.Trim('_');
        // Ensure it's not empty
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "unnamed_table"; // Fallback name
        }
        // Limit length if necessary (PostgreSQL default is 63 characters)
        if (sanitized.Length > 63)
        {
            sanitized = sanitized.Substring(0, 63);
        }
        return sanitized.ToLowerInvariant(); // PostgreSQL usually prefers lowercase identifiers
    }

    /// <summary>
    /// Sanitizes a string to be a valid PostgreSQL column name.
    /// Similar to table name sanitization.
    /// </summary>
    private static string SanitizeColumnName(string name)
    {
        // Replace non-alphanumeric characters (except underscore) with underscore
        string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]+", "_");
        // Ensure it doesn't start with a number
        if (char.IsDigit(sanitized.FirstOrDefault()))
        {
            sanitized = "_" + sanitized;
        }
        sanitized = sanitized.Trim('_');
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "column"; // Fallback name
        }
        if (sanitized.Length > 63) // PostgreSQL identifier limit
        {
            sanitized = sanitized.Substring(0, 63);
        }
        return sanitized.ToLowerInvariant();
    }
}
