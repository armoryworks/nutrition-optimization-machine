using System.Threading.Tasks;

namespace Nom.Import.Services
{
    /// <summary>
    /// Interface for AI services used in ingredient enhancement.
    /// </summary>
    public interface IAiService
    {
        /// <summary>
        /// Enhances ingredient data using AI.
        /// </summary>
        /// <param name="prompt">The prompt to send to the AI service.</param>
        /// <returns>The AI response.</returns>
        Task<string> EnhanceIngredientAsync(string prompt);
    }
}

