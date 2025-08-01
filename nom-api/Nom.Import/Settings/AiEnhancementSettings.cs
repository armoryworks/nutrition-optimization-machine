// File: nom-api/Nom.Import/Settings/AiEnhancementSettings.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Settings for AI-powered ingredient enhancement.
    /// </summary>
    public class AiEnhancementSettings
    {
        /// <summary>
        /// Whether to enable AI enhancement.
        /// </summary>
        public bool EnableAiEnhancement { get; set; } = false;

        /// <summary>
        /// The AI provider to use (OpenAI, Anthropic, Google, Azure, Ollama).
        /// </summary>
        public string AiProvider { get; set; } = "OpenAI";

        /// <summary>
        /// Number of ingredients to process in each batch.
        /// </summary>
        public int BatchSize { get; set; } = 10;

        /// <summary>
        /// Delay between batches in milliseconds.
        /// </summary>
        public int BatchDelayMs { get; set; } = 1000;

        /// <summary>
        /// Whether to preserve original names as aliases.
        /// </summary>
        public bool PreserveOriginalNamesAsAliases { get; set; } = true;

        /// <summary>
        /// Whether to update descriptions.
        /// </summary>
        public bool UpdateDescriptions { get; set; } = true;

        /// <summary>
        /// Whether to update names.
        /// </summary>
        public bool UpdateNames { get; set; } = true;

        /// <summary>
        /// Maximum number of ingredients to process (0 = unlimited).
        /// </summary>
        public int MaxIngredientsToProcess { get; set; } = 0;

        /// <summary>
        /// Quality threshold for AI enhancements.
        /// </summary>
        public double QualityThreshold { get; set; } = 0.5;
    }
} 