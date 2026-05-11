// ============================================================================
// 配置 Schema 定义
// ============================================================================
// 定义已知配置路径、类型和敏感路径，用于校验和脱敏。
// ============================================================================

namespace Autotrade.Cli.Config;

/// <summary>
/// 配置路径元信息。
/// </summary>
/// <param name="Type">值类型。</param>
/// <param name="Description">描述。</param>
/// <param name="DefaultValue">默认值（可选）。</param>
public sealed record ConfigPathInfo(
    Type Type,
    string Description,
    object? DefaultValue = null,
    bool IsOptional = false);

/// <summary>
/// 配置 Schema 定义。
/// 提供已知配置路径、敏感路径和校验功能。
/// </summary>
public static class ConfigSchema
{
    /// <summary>
    /// 已知配置路径及其元信息。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConfigPathInfo> KnownPaths = new Dictionary<string, ConfigPathInfo>(StringComparer.OrdinalIgnoreCase)
    {
        // StrategyEngine
        ["StrategyEngine:Enabled"] = new(typeof(bool), "是否启用策略引擎", true),
        ["StrategyEngine:MaxConcurrentStrategies"] = new(typeof(int), "最大并发策略数", 2),
        ["StrategyEngine:EvaluationIntervalSeconds"] = new(typeof(int), "策略评估间隔（秒）", 2),
        ["StrategyEngine:DecisionLogEnabled"] = new(typeof(bool), "是否启用决策日志", true),
        ["StrategyEngine:ConfigVersion"] = new(typeof(string), "配置版本"),
        ["StrategyObservations:Enabled"] = new(typeof(bool), "Enable structured strategy observations", true),
        ["StrategyObservations:AggregateSkips"] = new(typeof(bool), "Aggregate ordinary skip observations", true),
        ["StrategyObservations:SkipAggregationWindowSeconds"] = new(typeof(int), "Skip aggregation window in seconds", 60),
        ["StrategyObservations:SkipSampleEvery"] = new(typeof(int), "Sample one ordinary skip every N events", 100),

        ["StrategyEngine:MaxOrdersPerCycle"] = new(typeof(int), "Max strategy orders per evaluation cycle", 4),
        ["StrategyEngine:DesiredStates:dual_leg_arbitrage"] = new(typeof(string), "Desired state for dual_leg_arbitrage", "Stopped"),
        ["StrategyEngine:DesiredStates:endgame_sweep"] = new(typeof(string), "Desired state for endgame_sweep", "Stopped"),
        ["StrategyEngine:DesiredStates:liquidity_pulse"] = new(typeof(string), "Desired state for liquidity_pulse", "Stopped"),
        ["StrategyEngine:DesiredStates:liquidity_maker"] = new(typeof(string), "Desired state for liquidity_maker", "Stopped"),
        ["StrategyEngine:DesiredStates:micro_volatility_scalper"] = new(typeof(string), "Desired state for micro_volatility_scalper", "Stopped"),

        // DualLegArbitrage
        ["Strategies:DualLegArbitrage:Enabled"] = new(typeof(bool), "是否启用双腿套利策略", false),
        ["Strategies:DualLegArbitrage:PairCostThreshold"] = new(typeof(decimal), "配对成本阈值", 0.98m),
        ["Strategies:DualLegArbitrage:MinLiquidity"] = new(typeof(decimal), "最小流动性", 1000m),
        ["Strategies:DualLegArbitrage:MaxNotionalPerMarket"] = new(typeof(decimal), "每市场最大名义金额", 50m),
        ["Strategies:DualLegArbitrage:MaxNotionalPerOrder"] = new(typeof(decimal), "每订单最大名义金额", 10m),
        ["Strategies:DualLegArbitrage:HedgeTimeoutSeconds"] = new(typeof(int), "对冲超时（秒）", 30),

        // EndgameSweep
        ["Strategies:EndgameSweep:Enabled"] = new(typeof(bool), "是否启用尾盘扫货策略", false),
        ["Strategies:EndgameSweep:MinWinProbability"] = new(typeof(decimal), "最小胜率阈值", 0.90m),
        ["Strategies:EndgameSweep:MaxEntryPrice"] = new(typeof(decimal), "最大入场价格", 0.98m),

        // LiquidityPulse
        ["Strategies:LiquidityPulse:Enabled"] = new(typeof(bool), "Enable liquidity pulse strategy", false),
        ["Strategies:LiquidityPulse:MinLiquidity"] = new(typeof(decimal), "Minimum market liquidity", 2500m),
        ["Strategies:LiquidityPulse:MinVolume24h"] = new(typeof(decimal), "Minimum 24h volume", 500m),
        ["Strategies:LiquidityPulse:MaxMarkets"] = new(typeof(int), "Maximum subscribed markets", 40),
        ["Strategies:LiquidityPulse:MaxSpread"] = new(typeof(decimal), "Maximum top-book spread", 0.05m),
        ["Strategies:LiquidityPulse:MinBidDominance"] = new(typeof(decimal), "Minimum bid-depth dominance", 0.58m),
        ["Strategies:LiquidityPulse:MaxNotionalPerMarket"] = new(typeof(decimal), "Maximum notional per market", 25m),
        ["Strategies:LiquidityPulse:MaxNotionalPerOrder"] = new(typeof(decimal), "Maximum notional per order", 5m),

        // LiquidityMaker
        ["Strategies:LiquidityMaker:Enabled"] = new(typeof(bool), "Enable liquidity maker strategy", false),
        ["Strategies:LiquidityMaker:MinLiquidity"] = new(typeof(decimal), "Minimum market liquidity", 2500m),
        ["Strategies:LiquidityMaker:MinVolume24h"] = new(typeof(decimal), "Minimum 24h volume", 1000m),
        ["Strategies:LiquidityMaker:MaxMarkets"] = new(typeof(int), "Maximum subscribed markets", 30),
        ["Strategies:LiquidityMaker:MinSpread"] = new(typeof(decimal), "Minimum quoted spread", 0.015m),
        ["Strategies:LiquidityMaker:MaxSpread"] = new(typeof(decimal), "Maximum quoted spread", 0.08m),
        ["Strategies:LiquidityMaker:QuoteImproveTicks"] = new(typeof(decimal), "Bid improvement over best bid", 0.001m),
        ["Strategies:LiquidityMaker:MaxPassiveOrderAgeSeconds"] = new(typeof(int), "Passive order timeout in seconds", 120),
        ["Strategies:LiquidityMaker:MaxNotionalPerMarket"] = new(typeof(decimal), "Maximum notional per market", 20m),
        ["Strategies:LiquidityMaker:MaxNotionalPerOrder"] = new(typeof(decimal), "Maximum notional per order", 4m),

        // MicroVolatilityScalper
        ["Strategies:MicroVolatilityScalper:Enabled"] = new(typeof(bool), "Enable micro volatility scalper strategy", false),
        ["Strategies:MicroVolatilityScalper:MinLiquidity"] = new(typeof(decimal), "Minimum market liquidity", 1500m),
        ["Strategies:MicroVolatilityScalper:MinVolume24h"] = new(typeof(decimal), "Minimum 24h volume", 500m),
        ["Strategies:MicroVolatilityScalper:MaxMarkets"] = new(typeof(int), "Maximum subscribed markets", 35),
        ["Strategies:MicroVolatilityScalper:MaxSpread"] = new(typeof(decimal), "Maximum top-book spread", 0.06m),
        ["Strategies:MicroVolatilityScalper:SampleWindowSize"] = new(typeof(int), "Rolling mid-price sample window", 8),
        ["Strategies:MicroVolatilityScalper:MinSamples"] = new(typeof(int), "Minimum samples before entry", 4),
        ["Strategies:MicroVolatilityScalper:MinDipFromAverage"] = new(typeof(decimal), "Minimum dip from rolling mid average", 0.025m),
        ["Strategies:MicroVolatilityScalper:MaxNotionalPerMarket"] = new(typeof(decimal), "Maximum notional per market", 20m),
        ["Strategies:MicroVolatilityScalper:MaxNotionalPerOrder"] = new(typeof(decimal), "Maximum notional per order", 4m),

        // Execution
        ["Execution:Mode"] = new(typeof(string), "执行模式 (Live/Paper)", "Paper"),
        ["Execution:MaxOpenOrdersPerMarket"] = new(typeof(int), "每市场最大挂单数", 10),

        // RiskControl（跨进程控制面）
        ["RiskControl:KillSwitch:GlobalActive"] = new(typeof(bool), "是否激活全局 Kill Switch（由运行中进程自动对齐）", false),
        ["RiskControl:KillSwitch:GlobalLevel"] = new(typeof(string), "全局 Kill Switch 级别 (SoftStop/HardStop)", "HardStop"),
        ["RiskControl:KillSwitch:GlobalReasonCode"] = new(typeof(string), "全局 Kill Switch 原因代码", "MANUAL"),
        ["RiskControl:KillSwitch:GlobalReason"] = new(typeof(string), "全局 Kill Switch 原因描述", "Manual kill switch"),
        ["RiskControl:KillSwitch:GlobalContextJson"] = new(typeof(string), "全局 Kill Switch 上下文 JSON（可选）", IsOptional: true),
        ["RiskControl:KillSwitch:GlobalResetToken"] = new(typeof(string), "全局 Kill Switch 重置令牌（写入新值触发一次 reset）", IsOptional: true),

        ["Execution:EnableReconciliation"] = new(typeof(bool), "Enable Live order reconciliation", true),
        ["Execution:ReconciliationIntervalSeconds"] = new(typeof(int), "Live order reconciliation interval in seconds", 60),
        ["Execution:UseBatchOrders"] = new(typeof(bool), "Use exchange batch order endpoint when available", false),
        ["Execution:MaxBatchOrderSize"] = new(typeof(int), "Maximum exchange batch order size", 15),
        ["Execution:EnableUserOrderEvents"] = new(typeof(bool), "Enable CLOB user order/trade events in Live mode", true),
        ["Execution:UserOrderEventSubscriptionRefreshSeconds"] = new(typeof(int), "Refresh interval for Live user order event subscriptions", 10),

        // Compliance
        ["Compliance:Enabled"] = new(typeof(bool), "Enable compliance guardrails", true),
        ["Compliance:GeoKycAllowed"] = new(typeof(bool), "Explicit confirmation that Live trading is allowed for this operator and venue", false),
        ["Compliance:AllowUnsafeLiveParameters"] = new(typeof(bool), "Allow unsafe Live parameter overrides with audit warnings", false),
        ["Compliance:MinLiveEvaluationIntervalSeconds"] = new(typeof(int), "Minimum Live strategy evaluation interval", 1),
        ["Compliance:MaxLiveOrdersPerCycle"] = new(typeof(int), "Maximum Live orders per strategy cycle", 10),
        ["Compliance:MaxLiveOpenOrders"] = new(typeof(int), "Maximum Live open orders", 100),
        ["Compliance:MaxLiveOpenOrdersPerMarket"] = new(typeof(int), "Maximum Live open orders per market", 20),
        ["Compliance:MinLiveReconciliationIntervalSeconds"] = new(typeof(int), "Minimum Live reconciliation interval", 5),
        ["Compliance:MaxLiveCapitalPerMarket"] = new(typeof(decimal), "Maximum Live capital usage per market", 0.25m),
        ["Compliance:MaxLiveCapitalPerStrategy"] = new(typeof(decimal), "Maximum Live capital usage per strategy", 0.50m),
        ["Compliance:MaxLiveTotalCapitalUtilization"] = new(typeof(decimal), "Maximum Live total capital utilization", 0.80m),

        // Risk
        ["Risk:MaxOpenOrders"] = new(typeof(int), "Risk maximum open orders", 20),
        ["Risk:MaxCapitalPerMarket"] = new(typeof(decimal), "Risk maximum capital per market", 0.05m),
        ["Risk:MaxCapitalPerStrategy"] = new(typeof(decimal), "Risk maximum capital per strategy", 0.30m),
        ["Risk:MaxTotalCapitalUtilization"] = new(typeof(decimal), "Risk maximum total capital utilization", 0.50m),

        // SelfImprove
        ["SelfImprove:Enabled"] = new(typeof(bool), "Enable SelfImprove context", false),
        ["SelfImprove:LiveAutoApplyEnabled"] = new(typeof(bool), "Enable generated strategy Live canary auto-apply gates", false),
        ["SelfImprove:ArtifactRoot"] = new(typeof(string), "SelfImprove immutable artifact root", "artifacts/self-improve"),
        ["SelfImprove:Llm:Provider"] = new(typeof(string), "SelfImprove LLM provider", "OpenAICompatible"),
        ["SelfImprove:Llm:Model"] = new(typeof(string), "SelfImprove LLM model", "gpt-4.1-mini"),
        ["SelfImprove:Llm:BaseUrl"] = new(typeof(string), "OpenAI-compatible base URL", "", IsOptional: true),
        ["SelfImprove:Llm:ApiKeyEnvVar"] = new(typeof(string), "Environment variable containing the LLM API key", "OPENAI_API_KEY"),
        ["SelfImprove:Llm:TimeoutSeconds"] = new(typeof(int), "LLM request timeout", 120),
        ["SelfImprove:Llm:MaxRetries"] = new(typeof(int), "LLM retry count", 3),
        ["SelfImprove:CodeGen:Enabled"] = new(typeof(bool), "Enable LLM generated strategy packages", true),
        ["SelfImprove:CodeGen:PythonExecutable"] = new(typeof(string), "Python executable for generated strategy static validation", "python"),
        ["SelfImprove:CodeGen:PythonDllPath"] = new(typeof(string), "Optional pythonnet PYTHONNET_PYDLL path for the PythonScript worker", "", IsOptional: true),
        ["SelfImprove:CodeGen:DotnetExecutable"] = new(typeof(string), ".NET executable used to launch the PythonScript worker", "dotnet"),
        ["SelfImprove:CodeGen:WorkerAssemblyPath"] = new(typeof(string), "Optional explicit Autotrade.SelfImprove.PythonWorker.dll path", "", IsOptional: true),
        ["SelfImprove:CodeGen:WorkerTimeoutSeconds"] = new(typeof(int), "Python worker timeout", 5),
        ["SelfImprove:Canary:MaxActiveLiveCanaries"] = new(typeof(int), "Maximum active generated Live canaries", 1),
        ["SelfImprove:Canary:MaxSingleOrderNotional"] = new(typeof(decimal), "Generated Live canary single-order notional cap", 5m),
        ["SelfImprove:Canary:MaxCycleNotional"] = new(typeof(decimal), "Generated Live canary cycle notional cap", 20m),
        ["SelfImprove:Canary:MaxTotalNotional"] = new(typeof(decimal), "Generated Live canary total notional cap", 100m),
        ["ConfigurationMutation:BasePath"] = new(typeof(string), "Base JSON configuration path", "appsettings.json"),
        ["ConfigurationMutation:OverridePath"] = new(typeof(string), "Override JSON configuration path", "appsettings.local.json"),

        // Polymarket
        ["Polymarket:Clob:Host"] = new(typeof(string), "CLOB API 地址", "https://clob.polymarket.com"),
        ["Polymarket:Clob:ChainId"] = new(typeof(int), "链 ID", 137),
        ["Polymarket:Clob:Timeout"] = new(typeof(string), "请求超时", "00:00:10"),

        // Polymarket Gamma（市场元数据）
        ["Polymarket:Gamma:Host"] = new(typeof(string), "Gamma API 地址", "https://gamma-api.polymarket.com"),
        ["Polymarket:Gamma:Timeout"] = new(typeof(string), "Gamma 请求超时", "00:00:30"),

        // MarketData
        ["MarketData:CatalogSync:Enabled"] = new(typeof(bool), "是否启用市场目录同步（Gamma -> MarketCatalog）", true),
        ["MarketData:CatalogSync:RefreshIntervalSeconds"] = new(typeof(int), "市场目录刷新间隔（秒）", 300),
        ["MarketData:CatalogSync:PageSize"] = new(typeof(int), "Gamma /markets 分页大小", 100),
        ["MarketData:CatalogSync:MaxPages"] = new(typeof(int), "最大分页页数（防止拉取过量）", 200),
        ["MarketData:CatalogSync:IncludeClosed"] = new(typeof(bool), "是否包含 closed 市场", false),

        // Observability
        ["Observability:EnableTracing"] = new(typeof(bool), "是否启用追踪", true),
        ["Observability:EnableMetrics"] = new(typeof(bool), "是否启用指标", true),
        ["Observability:EnableOtlpExporter"] = new(typeof(bool), "是否启用 OTLP Exporter", false),
        ["Observability:OtlpEndpoint"] = new(typeof(string), "OTLP 端点", "http://localhost:4317"),
        ["Observability:EnablePrometheusExporter"] = new(typeof(bool), "是否启用 Prometheus Exporter", false),
        ["Observability:PrometheusPort"] = new(typeof(int), "Prometheus 监听端口", 9464),
        ["Observability:ServiceName"] = new(typeof(string), "服务名称", "Autotrade"),

        // Diagnostics
        ["Diagnostics:Enabled"] = new(typeof(bool), "是否启用诊断服务", true),
        ["Diagnostics:CheckIntervalSeconds"] = new(typeof(int), "诊断检查间隔（秒）", 30),
        ["Diagnostics:ApiLatencyWarningMs"] = new(typeof(int), "API 延迟警告阈值（毫秒）", 500),
        ["Diagnostics:ApiLatencyCriticalMs"] = new(typeof(int), "API 延迟临界阈值（毫秒）", 2000),
        ["Diagnostics:WsHeartbeatWarningSeconds"] = new(typeof(int), "WebSocket 心跳警告阈值（秒）", 30),
        ["Diagnostics:WsHeartbeatCriticalSeconds"] = new(typeof(int), "WebSocket 心跳临界阈值（秒）", 60),
        ["Diagnostics:StrategyLagWarningSeconds"] = new(typeof(int), "策略 lag 警告阈值（秒）", 10),
        ["Diagnostics:StrategyLagCriticalSeconds"] = new(typeof(int), "策略 lag 临界阈值（秒）", 30),
        ["Diagnostics:ErrorRateWarningPercent"] = new(typeof(double), "错误率警告阈值（百分比）", 5.0),
        ["Diagnostics:ErrorRateCriticalPercent"] = new(typeof(double), "错误率临界阈值（百分比）", 20.0),

        // HealthChecks
        ["HealthChecks:Api:LatencyWarningMs"] = new(typeof(int), "API 健康检查延迟警告阈值（毫秒）", 500),
        ["HealthChecks:Api:LatencyCriticalMs"] = new(typeof(int), "API 健康检查延迟临界阈值（毫秒）", 2000),
        ["HealthChecks:Api:TimeoutMs"] = new(typeof(int), "API 健康检查请求超时（毫秒）", 5000),
        ["HealthChecks:BackgroundService:HeartbeatWarningSeconds"] = new(typeof(int), "后台服务心跳警告阈值（秒）", 30),
        ["HealthChecks:BackgroundService:HeartbeatCriticalSeconds"] = new(typeof(int), "后台服务心跳临界阈值（秒）", 60),
    };

    /// <summary>
    /// 敏感配置路径（输出时脱敏）。
    /// </summary>
    public static readonly HashSet<string> SensitivePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "Polymarket:Clob:PrivateKey",
        "Polymarket:Clob:ApiKey",
        "Polymarket:Clob:ApiSecret",
        "Polymarket:Clob:ApiPassphrase",
        "ConnectionStrings:AutotradeDatabase"
    };

    /// <summary>
    /// 脱敏占位符。
    /// </summary>
    public const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>
    /// 检查路径是否为敏感路径。
    /// </summary>
    public static bool IsSensitive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // 精确匹配
        if (SensitivePaths.Contains(path))
        {
            return true;
        }

        // 前缀匹配（处理子路径）
        foreach (var sensitivePath in SensitivePaths)
        {
            if (path.StartsWith(sensitivePath + ":", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查路径是否为已知配置路径。
    /// </summary>
    public static bool IsKnownPath(string path) =>
        KnownPaths.ContainsKey(path);

    /// <summary>
    /// 获取路径的元信息。
    /// </summary>
    public static ConfigPathInfo? GetPathInfo(string path) =>
        KnownPaths.TryGetValue(path, out var info) ? info : null;

    /// <summary>
    /// 校验值类型是否匹配。
    /// </summary>
    public static bool ValidateType(string path, string rawValue, out string? error)
    {
        error = null;

        if (!KnownPaths.TryGetValue(path, out var info))
        {
            // 未知路径，不做类型校验
            return true;
        }

        try
        {
            if (info.Type == typeof(bool))
            {
                if (!bool.TryParse(rawValue, out _))
                {
                    error = $"期望布尔值 (true/false)，实际: {rawValue}";
                    return false;
                }
            }
            else if (info.Type == typeof(int))
            {
                if (!int.TryParse(rawValue, out _))
                {
                    error = $"期望整数，实际: {rawValue}";
                    return false;
                }
            }
            else if (info.Type == typeof(decimal))
            {
                if (!decimal.TryParse(rawValue, out _))
                {
                    error = $"期望小数，实际: {rawValue}";
                    return false;
                }
            }
            else if (info.Type == typeof(double))
            {
                if (!double.TryParse(rawValue, out _))
                {
                    error = $"期望双精度浮点数，实际: {rawValue}";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
