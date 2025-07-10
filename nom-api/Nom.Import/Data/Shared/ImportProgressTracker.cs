using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Import.Data.Shared
{
    /// <summary>
    /// Tracks import progress by storing stage offsets in a file in the /tmp directory.
    /// </summary>
    public class ImportProgressTracker
    {
        private readonly ILogger<ImportProgressTracker> _logger;
        private readonly string _progressFilePath;
        private Dictionary<string, long> _progressCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public ImportProgressTracker(ILogger<ImportProgressTracker> logger)
        {
            _logger = logger;
            // Define the path for the progress file in /tmp
            _progressFilePath = Path.Combine(Path.GetTempPath(), "nom_import_progress.log");
            LoadProgressFromFile();
        }

        /// <summary>
        /// Loads all stage progress from the progress file into memory.
        /// </summary>
        private void LoadProgressFromFile()
        {
            _progressCache.Clear();
            if (File.Exists(_progressFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(_progressFilePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && long.TryParse(parts[1].Trim(), out long offset))
                        {
                            _progressCache[parts[0].Trim()] = offset;
                        }
                    }
                    _logger.LogInformation("Loaded import progress from {FilePath}.", _progressFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading import progress from {FilePath}. Starting fresh.", _progressFilePath);
                    // If there's an error, treat it as if no progress was found
                    _progressCache.Clear();
                }
            }
            else
            {
                _logger.LogInformation("No existing import progress file found at {FilePath}.", _progressFilePath);
            }
        }

        /// <summary>
        /// Gets the last processed offset for a specific import stage.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <returns>The last processed offset, or 0 if no progress is recorded for this stage.</returns>
        public long GetLastProcessedOffset(string stageName)
        {
            if (_progressCache.TryGetValue(stageName, out long offset))
            {
                _logger.LogDebug("Retrieved last processed offset for stage '{StageName}': {Offset}", stageName, offset);
                return offset;
            }
            _logger.LogDebug("No previous offset found for stage '{StageName}'. Starting from 0.", stageName);
            return 0;
        }

        /// <summary>
        /// Updates the progress for a specific import stage and saves it to the file.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <param name="offset">The current processed offset for the stage.</param>
        public async Task UpdateProgressAsync(string stageName, long offset)
        {
            _progressCache[stageName] = offset;
            await SaveProgressToFileAsync();
            _logger.LogDebug("Updated progress for stage '{StageName}': {Offset}", stageName, offset);
        }

        /// <summary>
        /// Saves the current in-memory progress cache to the progress file.
        /// </summary>
        private async Task SaveProgressToFileAsync()
        {
            try
            {
                var lines = _progressCache.Select(kvp => $"{kvp.Key}:{kvp.Value}");
                await File.WriteAllLinesAsync(_progressFilePath, lines);
                _logger.LogDebug("Saved all import progress to {FilePath}.", _progressFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving import progress to {FilePath}.", _progressFilePath);
            }
        }

        /// <summary>
        /// Clears the progress for a specific stage from the tracker and file.
        /// </summary>
        /// <param name="stageName">The name of the stage to clear.</param>
        public async Task ClearStageProgressAsync(string stageName)
        {
            if (_progressCache.Remove(stageName))
            {
                _logger.LogInformation("Cleared progress for stage '{StageName}'.", stageName);
                await SaveProgressToFileAsync();
            }
        }

        /// <summary>
        /// Clears all import progress from the tracker and deletes the progress file.
        /// </summary>
        public async Task ClearAllProgressAsync()
        {
            _progressCache.Clear();
            if (File.Exists(_progressFilePath))
            {
                try
                {
                    File.Delete(_progressFilePath);
                    _logger.LogInformation("Cleared all import progress and deleted file {FilePath}.", _progressFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting progress file {FilePath}.", _progressFilePath);
                }
            }
            else
            {
                _logger.LogInformation("No progress file to delete at {FilePath}.", _progressFilePath);
            }
            await Task.CompletedTask; // For async consistency
        }
    }
}
