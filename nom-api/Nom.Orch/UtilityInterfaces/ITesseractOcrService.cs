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


} 