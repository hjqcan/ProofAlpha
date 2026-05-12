using System.Text.Json;

namespace Autotrade.Llm;

public sealed record LlmJsonRequest(
    string SystemPrompt,
    string UserPrompt,
    decimal Temperature = 0.1m);

public sealed record LlmJsonResult<T>(
    T Value,
    string RawJson,
    string RawText);

public interface ILlmJsonValidatable
{
    IReadOnlyList<string> Validate();
}

public interface ILlmJsonClient
{
    Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
        LlmJsonRequest request,
        Func<T, IReadOnlyList<string>>? validator = null,
        CancellationToken cancellationToken = default)
        where T : class;
}

public class LlmClientException : Exception
{
    public LlmClientException(string message) : base(message)
    {
    }

    public LlmClientException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

public sealed class LlmJsonValidationException : LlmClientException
{
    public LlmJsonValidationException(IReadOnlyList<string> errors)
        : base($"LLM JSON validation failed: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class OpenAiCompatibleLlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "OpenAICompatible";

    public string? EnvPrefix { get; set; }

    public string Model { get; set; } = "gpt-4.1-mini";

    public string? BaseUrl { get; set; }

    public string ApiKeyEnvVar { get; set; } = "OPENAI_API_KEY";

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxRetries { get; set; } = 3;

    public static OpenAiCompatibleLlmOptions FromJsonElement(JsonElement element)
    {
        var options = new OpenAiCompatibleLlmOptions();
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "provider":
                    options.Provider = property.Value.GetString() ?? options.Provider;
                    break;
                case "envprefix":
                    options.EnvPrefix = property.Value.GetString();
                    break;
                case "model":
                    options.Model = property.Value.GetString() ?? options.Model;
                    break;
                case "baseurl":
                    options.BaseUrl = property.Value.GetString();
                    break;
                case "apikeyenvvar":
                    options.ApiKeyEnvVar = property.Value.GetString() ?? options.ApiKeyEnvVar;
                    break;
                case "timeoutseconds":
                    options.TimeoutSeconds = property.Value.GetInt32();
                    break;
                case "maxretries":
                    options.MaxRetries = property.Value.GetInt32();
                    break;
            }
        }

        return options;
    }
}
