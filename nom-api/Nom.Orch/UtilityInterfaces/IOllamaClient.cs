namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Thin client for a self-hosted Ollama server (D-061 — local inference).
    /// Configured via Ai:OllamaUrl and Ai:Model. IsConfigured is false when no
    /// URL is set, so callers fall back to non-AI defaults.
    /// </summary>
    public interface IOllamaClient
    {
        bool IsConfigured { get; }

        /// <summary>Generate a completion. When jsonMode is true, the model is asked to return strict JSON.</summary>
        Task<string> GenerateAsync(string prompt, bool jsonMode = false, CancellationToken ct = default);
    }
}
