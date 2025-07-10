namespace Nom.Import.Models
{
    public class ImportConfig
    {
        public string FdcCsvBasePath { get; set; } = string.Empty;
        public int BatchSize { get; set; }
        public int DefaultDebugLimit { get; set; }
        public long SystemPersonId { get; set; }
    }
}
