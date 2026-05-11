using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Llm;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Application.Contract.Llm;
using Autotrade.SelfImprove.Application.Contract.Proposals;

namespace Autotrade.SelfImprove.Application.Llm;

public sealed class OpenAiCompatibleLlmClient : ILLmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmJsonClient _jsonClient;

    public OpenAiCompatibleLlmClient(ILlmJsonClient jsonClient)
    {
        _jsonClient = jsonClient ?? throw new ArgumentNullException(nameof(jsonClient));
    }

    public async Task<IReadOnlyList<ImprovementProposalDocument>> AnalyzeEpisodeAsync(
        StrategyEpisodeAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _jsonClient.CompleteJsonAsync<LlmProposalResponse>(
                new LlmJsonRequest(
                    "You are a trading strategy reviewer. Return only valid JSON matching the requested schema.",
                    BuildAnalysisPrompt(request)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Value.Proposals ?? Array.Empty<ImprovementProposalDocument>();
    }

    public async Task<GeneratedStrategySpec> GenerateStrategyAsync(
        ImprovementProposalDocument proposal,
        StrategyEpisodeDto episode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(episode);

        var response = await _jsonClient.CompleteJsonAsync<LlmGeneratedStrategyResponse>(
                new LlmJsonRequest(
                    "You are a safe strategy package generator. Return only valid JSON matching the requested schema.",
                    BuildCodeGenerationPrompt(proposal, episode)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Value.GeneratedStrategy
            ?? throw new LlmClientException("LLM response did not include generatedStrategy.");
    }

    private static string BuildAnalysisPrompt(StrategyEpisodeAnalysisRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze the trading strategy episode and return JSON:");
        sb.AppendLine("{\"proposals\":[{\"kind\":\"parameterPatch|generatedStrategy|manualInvestigation\",\"riskLevel\":\"low|medium|high|critical\",\"title\":\"...\",\"rationale\":\"...\",\"evidence\":[{\"source\":\"observation|decision|order|trade|risk|episode\",\"id\":\"...\",\"reason\":\"...\"}],\"expectedImpact\":\"...\",\"rollbackConditions\":[\"...\"],\"parameterPatches\":[{\"path\":\"Strategies:strategy:Field\",\"valueJson\":\"...\",\"reason\":\"...\",\"maxRelativeChange\":0.2}],\"generatedStrategy\":null}]}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Every non-manual proposal must include at least one concrete evidence id from the episode source ids.");
        sb.AppendLine("- Do not propose changes to secrets, compliance bypasses, kill switches, Execution:Mode, API keys, private keys, or direct order execution.");
        sb.AppendLine("- Generated strategy code must run out-of-process in Python and return only decision intents.");
        sb.AppendLine("Episode:");
        sb.AppendLine(JsonSerializer.Serialize(request.Episode, JsonOptions));
        sb.AppendLine("StrategyMemory:");
        sb.AppendLine(request.StrategyMemoryJson);
        sb.AppendLine("ImprovementPlaybook:");
        sb.AppendLine(request.ImprovementPlaybookJson);
        return sb.ToString();
    }

    private static string BuildCodeGenerationPrompt(ImprovementProposalDocument proposal, StrategyEpisodeDto episode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generate a complete immutable Python strategy package as JSON:");
        sb.AppendLine("{\"generatedStrategy\":{\"strategyId\":\"...\",\"name\":\"...\",\"description\":\"...\",\"pythonModule\":\"...\",\"manifestJson\":\"...\",\"parameterSchemaJson\":\"...\",\"unitTests\":\"...\",\"replaySpecJson\":\"...\",\"riskEnvelopeJson\":\"...\"}}");
        sb.AppendLine();
        sb.AppendLine("Constraints:");
        sb.AppendLine("- Python module must expose evaluate(input: dict) -> dict.");
        sb.AppendLine("- It must not import network, subprocess, os environment, exchange clients, or secret loaders.");
        sb.AppendLine("- It must return only action, reasonCode, reason, intents, telemetry, and statePatch.");
        sb.AppendLine("- Orders are intents only; C# risk and execution services remain authoritative.");
        sb.AppendLine();
        sb.AppendLine("Proposal:");
        sb.AppendLine(JsonSerializer.Serialize(proposal, JsonOptions));
        sb.AppendLine();
        sb.AppendLine("Episode:");
        sb.AppendLine(JsonSerializer.Serialize(episode, JsonOptions));
        return sb.ToString();
    }

    private sealed record LlmProposalResponse(IReadOnlyList<ImprovementProposalDocument>? Proposals);

    private sealed record LlmGeneratedStrategyResponse(GeneratedStrategySpec? GeneratedStrategy);
}
