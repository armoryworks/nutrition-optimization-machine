namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Tesseract OCR service
    /// </summary>
    public interface ITesseractOcrService
    {
        /// <summary>
        /// Processes an image with OCR to extract recipe data
        /// </summary>
        /// <param name="imageData">The image data</param>
        /// <returns>Extracted recipe data</returns>
        Task<OcrRecipeData> ProcessImageWithOcrAsync(byte[] imageData);
    }

    /// <summary>
    /// OCR recipe data structure
    /// </summary>
    public class OcrRecipeData
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Ingredients { get; set; } = new List<string>();
        public List<string> Instructions { get; set; } = new List<string>();
        public string PrepTime { get; set; } = string.Empty;
        public string CookTime { get; set; } = string.Empty;
        public string TotalTime { get; set; } = string.Empty;
        public string Yield { get; set; } = string.Empty;
        public string RawText { get; set; } = string.Empty;
    }
} 