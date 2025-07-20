// File: nom-api/Nom.Import/Settings/ImportSettings.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Represents the settings required for the data import process,
    /// typically configured in appsettings.json.
    /// </summary>
    public class ImportSettings
    {
        /// <summary>
        /// The directory path where the source CSV files for the import are located.
        /// </summary>
        public string SourceDirectory { get; set; } = string.Empty;
    }
}
