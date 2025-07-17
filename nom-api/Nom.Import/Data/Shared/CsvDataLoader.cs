using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices; // Required for EnumeratorCancellation

namespace Nom.Import.Data.Shared
{
    /// <summary>
    /// A generic utility for loading CSV data in batches.
    /// </summary>
    /// <typeparam name="TCsvModel">The type of the CSV model representing a row.</typeparam>
    public class CsvDataLoader<TCsvModel> where TCsvModel : class
    {
        private readonly ILogger<CsvDataLoader<TCsvModel>> _logger;

        public CsvDataLoader(ILogger<CsvDataLoader<TCsvModel>> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Asynchronously loads CSV records from a file in batches.
        /// </summary>
        /// <param name="filePath">The full path to the CSV file.</param>
        /// <param name="batchSize">The number of records per batch.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>An asynchronous enumerable of record batches.</returns>
        public async IAsyncEnumerable<List<TCsvModel>> LoadCsvInBatchesAsync(
            string filePath,
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) // ADDED [EnumeratorCancellation]
        {
            _logger.LogInformation("Starting to load CSV from {FilePath} in batches of {BatchSize}.", filePath, batchSize);

            if (!File.Exists(filePath))
            {
                _logger.LogError("CSV file not found at: {FilePath}", filePath);
                yield break; // Exit if file not found
            }

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null, // Do not throw if a field is missing
                BadDataFound = null,      // Do not throw on bad data, just log
                PrepareHeaderForMatch = args => args.Header.ToLowerInvariant() // Case-insensitive header matching
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            // Advance to the first record after the header
            await csv.ReadAsync();
            csv.ReadHeader();

            var batch = new List<TCsvModel>(batchSize);
            long recordCount = 0;

            // The try-catch block now wraps the entire reading loop,
            // allowing yield return to be outside the inner try-catch for GetRecord.
            // Individual record errors are logged and skipped.
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                TCsvModel? record = null;
                try
                {
                    record = csv.GetRecord<TCsvModel>();
                }
                catch (CsvHelperException ex)
                {
                    _logger.LogError(ex, "Error reading CSV record at row {Row} ({RawRecord}): {Message}",
                        csv.Context.Parser.Row, csv.Context.Parser.RawRecord, ex.Message);
                    // Continue to the next record if there's an error with this one
                    continue;
                }
                catch (Exception ex) // Catch any other unexpected exceptions during GetRecord
                {
                    _logger.LogError(ex, "An unexpected error occurred while getting CSV record at row {Row} ({RawRecord}): {Message}",
                        csv.Context.Parser.Row, csv.Context.Parser.RawRecord, ex.Message);
                    continue;
                }


                if (record != null)
                {
                    batch.Add(record);
                    recordCount++;

                    if (batch.Count >= batchSize)
                    {
                        _logger.LogDebug("Yielding batch of {Count} records. Total records read: {Total}", batch.Count, recordCount);
                        yield return batch;
                        batch = new List<TCsvModel>(batchSize); // Start a new batch
                    }
                }
            }

            // Yield any remaining records in the last batch
            if (batch.Any())
            {
                _logger.LogDebug("Yielding final batch of {Count} records. Total records read: {Total}", batch.Count, recordCount);
                yield return batch;
            }

            _logger.LogInformation("Finished loading CSV from {FilePath}. Total records read: {Total}.", filePath, recordCount);
        }
    }
}
