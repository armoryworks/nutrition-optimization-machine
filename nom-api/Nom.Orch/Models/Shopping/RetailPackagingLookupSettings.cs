namespace Nom.Orch.Models.Shopping;

public class RetailPackagingLookupSettings
{
    /// <summary>
    /// Whether AI-powered retail packaging lookup is enabled.
    /// When false, the lookup endpoint only returns existing DB matches.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// AI provider: "Anthropic", "OpenAI", "Ollama", or "none".
    /// </summary>
    public string AiProvider { get; set; } = "none";

    /// <summary>
    /// API key for cloud AI providers (Anthropic, OpenAI).
    /// Not needed for Ollama.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model name override. Defaults depend on provider:
    /// Anthropic: "claude-3-haiku-20240307", OpenAI: "gpt-4o-mini", Ollama: "llama3"
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Ollama base URL (default: http://localhost:11434).
    /// </summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Maximum ingredients to look up in a single AI call.
    /// </summary>
    public int MaxBatchSize { get; set; } = 25;

    /// <summary>
    /// Cooldown between AI lookups in seconds (rate limit).
    /// </summary>
    public int CooldownSeconds { get; set; } = 30;
}
