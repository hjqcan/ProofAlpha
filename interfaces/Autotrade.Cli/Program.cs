// ============================================================================
// Autotrade CLI 入口程序
// ============================================================================
// 这是 Autotrade 自动交易系统的命令行界面入口点，使用 System.CommandLine 构建命令树。
// 
// 系统架构：
// - MarketData 上下文：市场数据订阅、订单簿管理、行情同步
// - Trading 上下文：订单执行、风险管理、持仓跟踪
// - Strategy 上下文：策略引擎、决策日志、策略生命周期管理
// 
// 支持的命令：
// - run:         启动完整服务并持续运行
// - status:      查看系统状态（策略、KillSwitch 等）
// - health:      健康检查（liveness/readiness）
// - strategy:    策略管理（list/enable/disable/start/stop/pause/resume）
// - killswitch:  Kill Switch 控制（activate/reset）
// - positions:   持仓查询
// - orders:      订单查询
// - pnl:         PnL 查询
// - config:      配置读写（get/set/validate）
// - export:      数据导出（decisions/orders/trades/pnl/order-events）
// ============================================================================

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using Autotrade.Cli.Commands;
using Autotrade.Cli.Config;
using Autotrade.Cli.Infrastructure;
using Microsoft.Extensions.Hosting;

// ============================================================================
// 初始化
// ============================================================================

var startupCwd = Environment.CurrentDirectory;
var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? startupCwd;
Directory.SetCurrentDirectory(exeDir);
Directory.CreateDirectory("Logs");

using var activitySource = new ActivitySource("Autotrade");
using var activity = activitySource.StartActivity("CliEntry");

// ============================================================================
// 全局选项
// ============================================================================

var configOption = CreateOption<string?>("--config", "额外配置文件路径（覆盖默认配置）");
var jsonOption = CreateOptionWithDefault("--json", false, "以 JSON 输出");
var nonInteractiveOption = CreateOptionWithAliases(["--non-interactive", "-n"], false, "非交互模式，禁止所有确认提示");
var yesOption = CreateOptionWithAliases(["--yes", "-y"], false, "自动确认破坏性操作");
var noColorOption = CreateOptionWithDefault("--no-color", false, "禁用彩色输出");

// ============================================================================
// 根命令
// ============================================================================

var rootCommand = new RootCommand("Autotrade CLI");
AddGlobal(rootCommand, configOption);
AddGlobal(rootCommand, jsonOption);
AddGlobal(rootCommand, nonInteractiveOption);
AddGlobal(rootCommand, yesOption);
AddGlobal(rootCommand, noColorOption);

// ============================================================================
// run 命令
// ============================================================================

var runCommand = new Command("run", "启动完整服务并持续运行");
SetAction(runCommand, ExecuteRunAsync);

// ============================================================================
// status 命令
// ============================================================================

var statusCommand = new Command("status", "查看系统状态");
SetAction(statusCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithHostAsync(
            resolvedConfigPath,
            host => StatusCommand.ExecuteAsync(CreateContext(host, options)),
            suppressConsoleLogs: options.JsonOutput)
        .ConfigureAwait(false);
});

// ============================================================================
// health 命令
// ============================================================================

var healthCommand = new Command("health", "健康检查（liveness/readiness）");
var modeOption = CreateOptionWithDefault("--mode", "readiness", "liveness 或 readiness");
healthCommand.Add(modeOption);
SetAction(healthCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var mode = pr.GetValue(modeOption) ?? "readiness";
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithHostAsync(
            resolvedConfigPath,
            host => HealthCommand.ExecuteAsync(CreateContext(host, options), mode),
            suppressConsoleLogs: options.JsonOutput)
        .ConfigureAwait(false);
});

var readinessCommand = new Command("readiness", "First-run readiness diagnostics");
SetAction(readinessCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithHostAsync(
            resolvedConfigPath,
            host => ReadinessCommand.ExecuteAsync(CreateContext(host, options)),
            suppressConsoleLogs: options.JsonOutput)
        .ConfigureAwait(false);
});

// ============================================================================
// strategy 命令组
// ============================================================================

var liveCommand = new Command("live", "Live trading arming controls");

var liveStatusCommand = new Command("status", "Show Live arming status");
SetAction(liveStatusCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithHostAsync(
            resolvedConfigPath,
            host => LiveArmingCommand.StatusAsync(CreateContext(host, options)),
            suppressConsoleLogs: options.JsonOutput)
        .ConfigureAwait(false);
});

var liveArmActorOption = CreateOption<string?>("--actor", "Operator identifier");
var liveArmReasonOption = CreateOption<string?>("--reason", "Reason recorded in Live arming evidence");
var liveArmConfirmOption = CreateOption<string?>("--confirm", "Required confirmation text: ARM LIVE");
var liveArmCommand = new Command("arm", "Arm Live trading after all readiness gates pass");
liveArmCommand.Add(liveArmActorOption);
liveArmCommand.Add(liveArmReasonOption);
liveArmCommand.Add(liveArmConfirmOption);
SetAction(liveArmCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var actor = pr.GetValue(liveArmActorOption);
    var reason = pr.GetValue(liveArmReasonOption);
    var confirmationText = pr.GetValue(liveArmConfirmOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "live arm",
            new { actor, reason, confirmationProvided = !string.IsNullOrWhiteSpace(confirmationText) },
            resolvedConfigPath,
            host => LiveArmingCommand.ArmAsync(CreateContext(host, options), actor, reason, confirmationText))
        .ConfigureAwait(false);
});

var liveDisarmActorOption = CreateOption<string?>("--actor", "Operator identifier");
var liveDisarmReasonOption = CreateOption<string?>("--reason", "Reason recorded for Live disarming");
var liveDisarmConfirmOption = CreateOption<string?>("--confirm", "Required confirmation text: DISARM LIVE");
var liveDisarmCommand = new Command("disarm", "Disarm Live trading and clear current evidence");
liveDisarmCommand.Add(liveDisarmActorOption);
liveDisarmCommand.Add(liveDisarmReasonOption);
liveDisarmCommand.Add(liveDisarmConfirmOption);
SetAction(liveDisarmCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var actor = pr.GetValue(liveDisarmActorOption);
    var reason = pr.GetValue(liveDisarmReasonOption);
    var confirmationText = pr.GetValue(liveDisarmConfirmOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "live disarm",
            new { actor, reason, confirmationProvided = !string.IsNullOrWhiteSpace(confirmationText) },
            resolvedConfigPath,
            host => LiveArmingCommand.DisarmAsync(CreateContext(host, options), actor, reason, confirmationText))
        .ConfigureAwait(false);
});

liveCommand.Add(liveStatusCommand);
liveCommand.Add(liveArmCommand);
liveCommand.Add(liveDisarmCommand);

var strategyCommand = new Command("strategy", "策略管理");
var strategyIdOption = CreateRequiredOption<string>("--id", "策略 ID");

var strategyListCommand = new Command("list", "列出策略状态");
SetAction(strategyListCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "strategy list",
            new { json = options.JsonOutput },
            resolvedConfigPath,
            host => StrategyCommands.ListAsync(CreateContext(host, options)))
        .ConfigureAwait(false);
});

var strategyEnableCommand = new Command("enable", "启用策略配置");
strategyEnableCommand.Add(strategyIdOption);
SetAction(strategyEnableCommand, pr => SetStrategyEnabledAsync(pr, true));

var strategyDisableCommand = new Command("disable", "禁用策略配置");
strategyDisableCommand.Add(strategyIdOption);
SetAction(strategyDisableCommand, pr => SetStrategyEnabledAsync(pr, false));

var strategyStartCommand = new Command("start", "启动策略");
strategyStartCommand.Add(strategyIdOption);
SetAction(strategyStartCommand, pr => SetDesiredStrategyStateAsync(pr, SystemCommands.StartStrategyAsync));

var strategyStopCommand = new Command("stop", "停止策略");
strategyStopCommand.Add(strategyIdOption);
SetAction(strategyStopCommand, pr => SetDesiredStrategyStateAsync(pr, SystemCommands.StopStrategyAsync));

var strategyPauseCommand = new Command("pause", "暂停策略");
strategyPauseCommand.Add(strategyIdOption);
SetAction(strategyPauseCommand, pr => SetDesiredStrategyStateAsync(pr, SystemCommands.PauseStrategyAsync));

var strategyResumeCommand = new Command("resume", "恢复策略");
strategyResumeCommand.Add(strategyIdOption);
SetAction(strategyResumeCommand, pr => SetDesiredStrategyStateAsync(pr, SystemCommands.ResumeStrategyAsync));

strategyCommand.Add(strategyListCommand);
strategyCommand.Add(strategyEnableCommand);
strategyCommand.Add(strategyDisableCommand);
strategyCommand.Add(strategyStartCommand);
strategyCommand.Add(strategyStopCommand);
strategyCommand.Add(strategyPauseCommand);
strategyCommand.Add(strategyResumeCommand);

// ============================================================================
// killswitch 命令组（跨进程控制面）
// ============================================================================

var killSwitchCommand = new Command("killswitch", "Kill Switch 控制（全局/策略级）");
var ksStrategyIdOption = CreateOption<string?>("--strategy-id", "策略 ID（可选，为空表示全局）");
var ksLevelOption = CreateOptionWithDefault("--level", "hard", "soft/hard 或 SoftStop/HardStop");
var ksReasonCodeOption = CreateOptionWithDefault("--reason-code", "MANUAL", "原因代码（用于审计/指标聚合）");
var ksReasonOption = CreateRequiredOption<string>("--reason", "原因描述");
var ksMarketIdOption = CreateOption<string?>("--market-id", "关联市场 ID（可选）");
var ksContextJsonOption = CreateOption<string?>("--context-json", "上下文 JSON（可选）");

var killSwitchActivateCommand = new Command("activate", "激活 Kill Switch（默认 HardStop，会撤单）");
killSwitchActivateCommand.Add(ksStrategyIdOption);
killSwitchActivateCommand.Add(ksLevelOption);
killSwitchActivateCommand.Add(ksReasonCodeOption);
killSwitchActivateCommand.Add(ksReasonOption);
killSwitchActivateCommand.Add(ksMarketIdOption);
killSwitchActivateCommand.Add(ksContextJsonOption);
SetAction(killSwitchActivateCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(ksStrategyIdOption);
    var level = pr.GetValue(ksLevelOption) ?? "hard";
    var reasonCode = pr.GetValue(ksReasonCodeOption) ?? "MANUAL";
    var reason = pr.GetRequiredValue(ksReasonOption);
    var marketId = pr.GetValue(ksMarketIdOption);
    var contextJson = pr.GetValue(ksContextJsonOption);

    return await CommandAuditService.ExecuteWithAuditAsync(
            "killswitch activate",
            new { strategyId, level, reasonCode, reason, marketId },
            resolvedConfigPath,
            host => Task.FromResult(KillSwitchCommands.Activate(
                CreateContext(host, options),
                CreateConfigService(resolvedConfigPath),
                strategyId,
                level,
                reasonCode,
                reason,
                marketId,
                contextJson)))
        .ConfigureAwait(false);
});

var killSwitchResetCommand = new Command("reset", "重置 Kill Switch（全局或策略级）");
killSwitchResetCommand.Add(ksStrategyIdOption);
SetAction(killSwitchResetCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(ksStrategyIdOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "killswitch reset",
            new { strategyId },
            resolvedConfigPath,
            host => Task.FromResult(KillSwitchCommands.Reset(
                CreateContext(host, options),
                CreateConfigService(resolvedConfigPath),
                strategyId)))
        .ConfigureAwait(false);
});

killSwitchCommand.Add(killSwitchActivateCommand);
killSwitchCommand.Add(killSwitchResetCommand);

// ============================================================================
// positions 命令
// ============================================================================

var positionsCommand = new Command("positions", "持仓查询");
SetAction(positionsCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "positions",
            new { },
            resolvedConfigPath,
            host => SystemCommands.ListPositionsAsync(CreateContext(host, options)))
        .ConfigureAwait(false);
});

// ============================================================================
// orders 命令
// ============================================================================

var ordersCommand = new Command("orders", "订单查询");
var ordersStrategyOption = CreateOption<string?>("--strategy-id", "策略 ID");
var ordersStatusOption = CreateOptionWithDefault<string?>("--status", "open", "订单状态 (open/all)");
ordersCommand.Add(ordersStrategyOption);
ordersCommand.Add(ordersStatusOption);
SetAction(ordersCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(ordersStrategyOption);
    var status = pr.GetValue(ordersStatusOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "orders",
            new { strategyId, status },
            resolvedConfigPath,
            host => SystemCommands.ListOrdersAsync(CreateContext(host, options), strategyId, status))
        .ConfigureAwait(false);
});

// ============================================================================
// pnl 命令
// ============================================================================

var pnlCommand = new Command("pnl", "PnL 查询");
var pnlStrategyOption = CreateOption<string?>("--strategy-id", "策略 ID");
var pnlFromOption = CreateOption<string?>("--from", "开始时间（ISO8601）");
var pnlToOption = CreateOption<string?>("--to", "结束时间（ISO8601）");
pnlCommand.Add(pnlStrategyOption);
pnlCommand.Add(pnlFromOption);
pnlCommand.Add(pnlToOption);
SetAction(pnlCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(pnlStrategyOption);
    var from = pr.GetValue(pnlFromOption);
    var to = pr.GetValue(pnlToOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "pnl",
            new { strategyId, from, to },
            resolvedConfigPath,
            host => SystemCommands.ShowPnLAsync(CreateContext(host, options), strategyId, from, to))
        .ConfigureAwait(false);
});

// ============================================================================
// config 命令组
// ============================================================================

var configCommand = new Command("config", "读取或写入配置");
var pathOption = CreateRequiredOption<string>("--path", "配置路径（例如 StrategyEngine:Enabled）");
var valueOption = CreateRequiredOption<string>("--value", "配置值");
var showSourceOption = CreateOptionWithDefault("--show-source", false, "显示配置值来源");

var configGetCommand = new Command("get", "读取配置值");
configGetCommand.Add(pathOption);
configGetCommand.Add(showSourceOption);
SetAction(configGetCommand, async pr =>
{
    var path = pr.GetRequiredValue(pathOption);
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var showSource = pr.GetValue(showSourceOption);
    return await CommandAuditService.ExecuteLocalAsync(
            () => Task.FromResult(ConfigCommands.Get(
                CreateConfigService(resolvedConfigPath),
                path,
                options.JsonOutput,
                showSource)))
        .ConfigureAwait(false);
});

var configSetCommand = new Command("set", "写入配置值");
configSetCommand.Add(pathOption);
configSetCommand.Add(valueOption);
SetAction(configSetCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var path = pr.GetRequiredValue(pathOption);
    var value = pr.GetRequiredValue(valueOption);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteLocalAsync(
            () => Task.FromResult(ConfigCommands.Set(
                CreateConfigService(resolvedConfigPath),
                path,
                value,
                options)))
        .ConfigureAwait(false);
});

var configValidateCommand = new Command("validate", "校验配置文件");
SetAction(configValidateCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteLocalAsync(
            () => Task.FromResult(ConfigCommands.Validate(
                CreateConfigService(resolvedConfigPath),
                options.JsonOutput)))
        .ConfigureAwait(false);
});

configCommand.Add(configGetCommand);
configCommand.Add(configSetCommand);
configCommand.Add(configValidateCommand);

// ============================================================================
// export 命令组
// ============================================================================

var exportCommand = new Command("export", "导出数据");
var exportStrategyOption = CreateOption<string?>("--strategy-id", "策略 ID");
var exportMarketOption = CreateOption<string?>("--market-id", "市场 ID");
var exportFromOption = CreateOption<string?>("--from", "开始时间（ISO8601）");
var exportToOption = CreateOption<string?>("--to", "结束时间（ISO8601）");
var exportLimitOption = CreateOptionWithDefault("--limit", 200, "最大条数");
var exportOutputOption = CreateOption<FileInfo?>("--output", "输出文件（可选）");
var exportSessionIdOption = CreateRequiredOption<Guid>("--session-id", "Paper run session ID");
var exportReplaySessionIdOption = CreateOption<Guid?>("--session-id", "Paper run session ID");
var exportOrderIdOption = CreateOption<Guid?>("--order-id", "Order ID");
var exportClientOrderIdOption = CreateOption<string?>("--client-order-id", "Client order ID");
var exportRiskEventIdOption = CreateOption<Guid?>("--risk-event-id", "Risk event ID");
var exportCorrelationIdOption = CreateOption<string?>("--correlation-id", "Correlation ID");

var exportDecisionsCommand = CreateExportCommand("decisions", "导出策略决策日志", includeMarket: true, ExportDecisionsAsync);
var exportOrdersCommand = CreateExportCommand("orders", "导出订单记录", includeMarket: true, ExportOrdersAsync);
var exportTradesCommand = CreateExportCommand("trades", "导出成交记录", includeMarket: true, ExportTradesAsync);
var exportPnlCommand = new Command("pnl", "导出 PnL 汇总");
exportPnlCommand.Add(exportStrategyOption);
exportPnlCommand.Add(exportFromOption);
exportPnlCommand.Add(exportToOption);
exportPnlCommand.Add(exportOutputOption);
SetAction(exportPnlCommand, ExportPnlAsync);
var exportOrderEventsCommand = CreateExportCommand("order-events", "导出订单事件（审计日志）", includeMarket: true, ExportOrderEventsAsync);
var exportRunReportCommand = new Command("run-report", "Export Paper run report");
exportRunReportCommand.Add(exportSessionIdOption);
exportRunReportCommand.Add(exportLimitOption);
exportRunReportCommand.Add(exportOutputOption);
SetAction(exportRunReportCommand, ExportRunReportAsync);
var exportPromotionChecklistCommand = new Command("promotion-checklist", "Export Paper-to-Live promotion checklist");
exportPromotionChecklistCommand.Add(exportSessionIdOption);
exportPromotionChecklistCommand.Add(exportLimitOption);
exportPromotionChecklistCommand.Add(exportOutputOption);
SetAction(exportPromotionChecklistCommand, ExportPromotionChecklistAsync);
var exportReplayPackageCommand = new Command("replay-package", "Export offline replay evidence package");
exportReplayPackageCommand.Add(exportStrategyOption);
exportReplayPackageCommand.Add(exportMarketOption);
exportReplayPackageCommand.Add(exportOrderIdOption);
exportReplayPackageCommand.Add(exportClientOrderIdOption);
exportReplayPackageCommand.Add(exportReplaySessionIdOption);
exportReplayPackageCommand.Add(exportRiskEventIdOption);
exportReplayPackageCommand.Add(exportCorrelationIdOption);
exportReplayPackageCommand.Add(exportFromOption);
exportReplayPackageCommand.Add(exportToOption);
exportReplayPackageCommand.Add(exportLimitOption);
exportReplayPackageCommand.Add(exportOutputOption);
SetAction(exportReplayPackageCommand, ExportReplayPackageAsync);

exportCommand.Add(exportDecisionsCommand);
exportCommand.Add(exportOrdersCommand);
exportCommand.Add(exportTradesCommand);
exportCommand.Add(exportPnlCommand);
exportCommand.Add(exportOrderEventsCommand);
exportCommand.Add(exportRunReportCommand);
exportCommand.Add(exportPromotionChecklistCommand);
exportCommand.Add(exportReplayPackageCommand);

// ============================================================================
// self-improve command group
// ============================================================================

var selfImproveCommand = new Command("self-improve", "SelfImprove analysis, patches, and generated strategy gates");
var siStrategyIdOption = CreateRequiredOption<string>("--strategy-id", "Strategy ID");
var siMarketIdOption = CreateOption<string?>("--market-id", "Market ID filter");
var siFromOption = CreateOption<string?>("--from", "Window start time (ISO8601)");
var siToOption = CreateOption<string?>("--to", "Window end time (ISO8601)");
var siWindowMinutesOption = CreateOptionWithDefault("--window-minutes", 60, "Lookback window in minutes when --from is omitted");
var siLimitOption = CreateOptionWithDefault("--limit", 50, "Maximum records");
var siRunIdOption = CreateRequiredOption<Guid>("--run-id", "SelfImprove run ID");
var siProposalIdOption = CreateRequiredOption<Guid>("--proposal-id", "Improvement proposal ID");
var siGeneratedVersionIdOption = CreateRequiredOption<Guid>("--generated-version-id", "Generated strategy version ID");
var siDryRunOption = CreateOptionWithDefault("--dry-run", true, "Validate patch without writing config");
var siStageOption = CreateRequiredOption<string>("--stage", "Target generated strategy stage");
var siReasonOption = CreateRequiredOption<string>("--reason", "Reason");

var selfImproveRunCommand = new Command("run", "Build an episode and ask the LLM for proposals");
selfImproveRunCommand.Add(siStrategyIdOption);
selfImproveRunCommand.Add(siMarketIdOption);
selfImproveRunCommand.Add(siFromOption);
selfImproveRunCommand.Add(siToOption);
selfImproveRunCommand.Add(siWindowMinutesOption);
SetAction(selfImproveRunCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve run",
            new { strategyId = pr.GetRequiredValue(siStrategyIdOption), marketId = pr.GetValue(siMarketIdOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.RunAsync(
                CreateContext(host, options),
                pr.GetRequiredValue(siStrategyIdOption),
                pr.GetValue(siMarketIdOption),
                pr.GetValue(siFromOption),
                pr.GetValue(siToOption),
                pr.GetValue(siWindowMinutesOption)))
        .ConfigureAwait(false);
});

var selfImproveListCommand = new Command("list", "List SelfImprove runs");
selfImproveListCommand.Add(siLimitOption);
SetAction(selfImproveListCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve list",
            new { limit = pr.GetValue(siLimitOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.ListAsync(CreateContext(host, options), pr.GetValue(siLimitOption)))
        .ConfigureAwait(false);
});

var selfImproveShowCommand = new Command("show", "Show a SelfImprove run");
selfImproveShowCommand.Add(siRunIdOption);
SetAction(selfImproveShowCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve show",
            new { runId = pr.GetValue(siRunIdOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.ShowAsync(CreateContext(host, options), pr.GetValue(siRunIdOption)))
        .ConfigureAwait(false);
});

var selfImproveApplyCommand = new Command("apply", "Apply or dry-run a parameter patch proposal");
selfImproveApplyCommand.Add(siProposalIdOption);
selfImproveApplyCommand.Add(siDryRunOption);
SetAction(selfImproveApplyCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve apply",
            new { proposalId = pr.GetValue(siProposalIdOption), dryRun = pr.GetValue(siDryRunOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.ApplyAsync(
                CreateContext(host, options),
                pr.GetValue(siProposalIdOption),
                pr.GetValue(siDryRunOption)))
        .ConfigureAwait(false);
});

var selfImprovePromoteCommand = new Command("promote", "Advance a generated strategy through a promotion gate");
selfImprovePromoteCommand.Add(siGeneratedVersionIdOption);
selfImprovePromoteCommand.Add(siStageOption);
SetAction(selfImprovePromoteCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve promote",
            new { generatedVersionId = pr.GetValue(siGeneratedVersionIdOption), stage = pr.GetRequiredValue(siStageOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.PromoteAsync(
                CreateContext(host, options),
                pr.GetValue(siGeneratedVersionIdOption),
                pr.GetRequiredValue(siStageOption)))
        .ConfigureAwait(false);
});

var selfImproveRollbackCommand = new Command("rollback", "Rollback a generated strategy version");
selfImproveRollbackCommand.Add(siGeneratedVersionIdOption);
SetAction(selfImproveRollbackCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve rollback",
            new { generatedVersionId = pr.GetValue(siGeneratedVersionIdOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.RollbackAsync(CreateContext(host, options), pr.GetValue(siGeneratedVersionIdOption)))
        .ConfigureAwait(false);
});

var selfImproveQuarantineCommand = new Command("quarantine", "Quarantine a generated strategy version");
selfImproveQuarantineCommand.Add(siGeneratedVersionIdOption);
selfImproveQuarantineCommand.Add(siReasonOption);
SetAction(selfImproveQuarantineCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "self-improve quarantine",
            new { generatedVersionId = pr.GetValue(siGeneratedVersionIdOption), reason = pr.GetRequiredValue(siReasonOption) },
            resolvedConfigPath,
            host => SelfImproveCommands.QuarantineAsync(
                CreateContext(host, options),
                pr.GetValue(siGeneratedVersionIdOption),
                pr.GetRequiredValue(siReasonOption)))
        .ConfigureAwait(false);
});

selfImproveCommand.Add(selfImproveRunCommand);
selfImproveCommand.Add(selfImproveListCommand);
selfImproveCommand.Add(selfImproveShowCommand);
selfImproveCommand.Add(selfImproveApplyCommand);
selfImproveCommand.Add(selfImprovePromoteCommand);
selfImproveCommand.Add(selfImproveRollbackCommand);
selfImproveCommand.Add(selfImproveQuarantineCommand);

// ============================================================================
// opportunity 命令组
// ============================================================================

var opportunityCommand = new Command("opportunity", "OpportunityDiscovery scan, review, and publishing");
var opportunityMinVolumeOption = CreateOptionWithDefault("--min-volume-24h", 500m, "Minimum 24h market volume");
var opportunityMinLiquidityOption = CreateOptionWithDefault("--min-liquidity", 500m, "Minimum market liquidity");
var opportunityMaxMarketsOption = CreateOptionWithDefault("--max-markets", 20, "Maximum markets to scan");
var opportunityStatusOption = CreateOption<string?>("--status", "Candidate/NeedsReview/Approved/Rejected/Published/Expired");
var opportunityLimitOption = CreateOptionWithDefault("--limit", 50, "Maximum records");
var opportunityIdOption = CreateRequiredOption<Guid>("--id", "Opportunity ID");
var opportunityActorOption = CreateOptionWithDefault("--actor", "cli", "Review actor");
var opportunityNotesOption = CreateOption<string?>("--notes", "Review notes");

var opportunityScanCommand = new Command("scan", "Scan active Polymarket markets for opportunities");
opportunityScanCommand.Add(opportunityMinVolumeOption);
opportunityScanCommand.Add(opportunityMinLiquidityOption);
opportunityScanCommand.Add(opportunityMaxMarketsOption);
SetAction(opportunityScanCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity scan",
            new
            {
                minVolume24h = pr.GetValue(opportunityMinVolumeOption),
                minLiquidity = pr.GetValue(opportunityMinLiquidityOption),
                maxMarkets = pr.GetValue(opportunityMaxMarketsOption)
            },
            resolvedConfigPath,
            host => OpportunityCommands.ScanAsync(
                CreateContext(host, options),
                pr.GetValue(opportunityMinVolumeOption),
                pr.GetValue(opportunityMinLiquidityOption),
                pr.GetValue(opportunityMaxMarketsOption)))
        .ConfigureAwait(false);
});

var opportunityListCommand = new Command("list", "List discovered opportunities");
opportunityListCommand.Add(opportunityStatusOption);
opportunityListCommand.Add(opportunityLimitOption);
SetAction(opportunityListCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity list",
            new { status = pr.GetValue(opportunityStatusOption), limit = pr.GetValue(opportunityLimitOption) },
            resolvedConfigPath,
            host => OpportunityCommands.ListAsync(
                CreateContext(host, options),
                pr.GetValue(opportunityStatusOption),
                pr.GetValue(opportunityLimitOption)))
        .ConfigureAwait(false);
});

var opportunityShowCommand = new Command("show", "Show one opportunity with cited evidence");
opportunityShowCommand.Add(opportunityIdOption);
SetAction(opportunityShowCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity show",
            new { opportunityId = pr.GetValue(opportunityIdOption) },
            resolvedConfigPath,
            host => OpportunityCommands.ShowAsync(CreateContext(host, options), pr.GetValue(opportunityIdOption)))
        .ConfigureAwait(false);
});

var opportunityApproveCommand = new Command("approve", "Approve an opportunity for publishing");
opportunityApproveCommand.Add(opportunityIdOption);
opportunityApproveCommand.Add(opportunityActorOption);
opportunityApproveCommand.Add(opportunityNotesOption);
SetAction(opportunityApproveCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity approve",
            new { opportunityId = pr.GetValue(opportunityIdOption), actor = pr.GetValue(opportunityActorOption) },
            resolvedConfigPath,
            host => OpportunityCommands.ApproveAsync(
                CreateContext(host, options),
                pr.GetValue(opportunityIdOption),
                pr.GetValue(opportunityActorOption) ?? "cli",
                pr.GetValue(opportunityNotesOption)))
        .ConfigureAwait(false);
});

var opportunityRejectCommand = new Command("reject", "Reject an opportunity");
opportunityRejectCommand.Add(opportunityIdOption);
opportunityRejectCommand.Add(opportunityActorOption);
opportunityRejectCommand.Add(opportunityNotesOption);
SetAction(opportunityRejectCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity reject",
            new { opportunityId = pr.GetValue(opportunityIdOption), actor = pr.GetValue(opportunityActorOption) },
            resolvedConfigPath,
            host => OpportunityCommands.RejectAsync(
                CreateContext(host, options),
                pr.GetValue(opportunityIdOption),
                pr.GetValue(opportunityActorOption) ?? "cli",
                pr.GetValue(opportunityNotesOption)))
        .ConfigureAwait(false);
});

var opportunityPublishCommand = new Command("publish", "Publish an approved opportunity to the strategy feed");
opportunityPublishCommand.Add(opportunityIdOption);
opportunityPublishCommand.Add(opportunityActorOption);
opportunityPublishCommand.Add(opportunityNotesOption);
SetAction(opportunityPublishCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "opportunity publish",
            new { opportunityId = pr.GetValue(opportunityIdOption), actor = pr.GetValue(opportunityActorOption) },
            resolvedConfigPath,
            host => OpportunityCommands.PublishAsync(
                CreateContext(host, options),
                pr.GetValue(opportunityIdOption),
                pr.GetValue(opportunityActorOption) ?? "cli",
                pr.GetValue(opportunityNotesOption)))
        .ConfigureAwait(false);
});

opportunityCommand.Add(opportunityScanCommand);
opportunityCommand.Add(opportunityListCommand);
opportunityCommand.Add(opportunityShowCommand);
opportunityCommand.Add(opportunityApproveCommand);
opportunityCommand.Add(opportunityRejectCommand);
opportunityCommand.Add(opportunityPublishCommand);

// ============================================================================
// account 命令组
// ============================================================================

var accountCommand = new Command("account", "账户管理命令");

var accountStatusCommand = new Command("status", "显示账户状态");
SetAction(accountStatusCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "account status",
            new { json = options.JsonOutput },
            resolvedConfigPath,
            host => AccountCommands.ExecuteStatusAsync(CreateContext(host, options)))
        .ConfigureAwait(false);
});

var failOnDriftOption = CreateOption<bool>("--fail-on-drift", "发现漂移时返回非零退出码");
var accountSyncCommand = new Command("sync", "手动触发账户同步");
accountSyncCommand.Add(failOnDriftOption);
SetAction(accountSyncCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var failOnDrift = pr.GetValue(failOnDriftOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "account sync",
            new { failOnDrift },
            resolvedConfigPath,
            host => AccountCommands.ExecuteSyncAsync(CreateContext(host, options), failOnDrift))
        .ConfigureAwait(false);
});

accountCommand.Add(accountStatusCommand);
accountCommand.Add(accountSyncCommand);

// ============================================================================
// arc command group
// ============================================================================

var arcCommand = new Command("arc", "Arc settlement and proof commands");
var arcSignalCommand = new Command("signal", "Arc signal proof publication commands");
var arcAccessCommand = new Command("access", "Arc subscription and entitlement commands");

var arcSignalProofFileOption = CreateOption<FileInfo?>("--proof-file", "Canonical Arc signal proof JSON file; omit to resolve --source/--id from local data");
var arcSignalSourceOption = CreateRequiredOption<string>("--source", "Source type: opportunity or decision");
var arcSignalIdOption = CreateRequiredOption<string>("--id", "Opportunity ID or strategy decision ID");
var arcSignalStatusOption = CreateOptionWithDefault("--status", "Approved", "Source review status");
var arcSignalActorOption = CreateRequiredOption<string>("--actor", "Operator publishing the signal");
var arcSignalReasonOption = CreateRequiredOption<string>("--reason", "Audit reason for publishing the signal");
var arcSignalSourcePolicyHashOption = CreateOption<string?>("--source-policy-hash", "Optional compiled source policy hash");
var arcSignalLimitOption = CreateOptionWithDefault("--limit", 20, "Maximum records to return");
var arcSignalSignalIdOption = CreateRequiredOption<string>("--signal-id", "Arc signal ID");
var arcAccessWalletOption = CreateRequiredOption<string>("--wallet", "Arc wallet address");
var arcAccessStrategyOption = CreateRequiredOption<string>("--strategy", "Strategy key");
var arcAccessPlanIdOption = CreateRequiredOption<int>("--plan-id", "Subscription plan id");
var arcAccessTxHashOption = CreateRequiredOption<string>("--tx-hash", "StrategySubscribed transaction hash");
var arcAccessExpiresAtOption = CreateRequiredOption<DateTimeOffset>("--expires-at", "Subscription expiry time in UTC");
var arcAccessBlockOption = CreateOption<long?>("--block", "Source block number");

var arcSignalPublishCommand = new Command("publish", "Publish a reviewed signal proof to Arc settlement");
arcSignalPublishCommand.Add(arcSignalProofFileOption);
arcSignalPublishCommand.Add(arcSignalSourceOption);
arcSignalPublishCommand.Add(arcSignalIdOption);
arcSignalPublishCommand.Add(arcSignalStatusOption);
arcSignalPublishCommand.Add(arcSignalActorOption);
arcSignalPublishCommand.Add(arcSignalReasonOption);
arcSignalPublishCommand.Add(arcSignalSourcePolicyHashOption);
SetAction(arcSignalPublishCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var proofFile = pr.GetValue(arcSignalProofFileOption);
    var source = pr.GetRequiredValue(arcSignalSourceOption);
    var sourceId = pr.GetRequiredValue(arcSignalIdOption);
    var status = pr.GetValue(arcSignalStatusOption) ?? "Approved";
    var actor = pr.GetRequiredValue(arcSignalActorOption);
    var reason = pr.GetRequiredValue(arcSignalReasonOption);
    var sourcePolicyHash = pr.GetValue(arcSignalSourcePolicyHashOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc signal publish",
            new { source, sourceId, status, actor, proofFile = proofFile?.FullName },
            resolvedConfigPath,
            host => ArcSignalCommands.PublishAsync(
                CreateContext(host, options),
                proofFile,
                source,
                sourceId,
                status,
                actor,
                reason,
                sourcePolicyHash))
        .ConfigureAwait(false);
});

var arcSignalListCommand = new Command("list", "List Arc signal publication records");
arcSignalListCommand.Add(arcSignalLimitOption);
SetAction(arcSignalListCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var limit = pr.GetValue(arcSignalLimitOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc signal list",
            new { limit },
            resolvedConfigPath,
            host => ArcSignalCommands.ListAsync(CreateContext(host, options), limit))
        .ConfigureAwait(false);
});

var arcSignalShowCommand = new Command("show", "Show one Arc signal publication record");
arcSignalShowCommand.Add(arcSignalSignalIdOption);
SetAction(arcSignalShowCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var signalId = pr.GetRequiredValue(arcSignalSignalIdOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc signal show",
            new { signalId },
            resolvedConfigPath,
            host => ArcSignalCommands.ShowAsync(CreateContext(host, options), signalId))
        .ConfigureAwait(false);
});

arcSignalCommand.Add(arcSignalPublishCommand);
arcSignalCommand.Add(arcSignalListCommand);
arcSignalCommand.Add(arcSignalShowCommand);

var arcAccessPlansCommand = new Command("plans", "List Arc subscription plans");
SetAction(arcAccessPlansCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc access plans",
            new { },
            resolvedConfigPath,
            host => ArcAccessCommands.PlansAsync(CreateContext(host, options)))
        .ConfigureAwait(false);
});

var arcAccessStatusCommand = new Command("status", "Show Arc access status for a wallet and strategy");
arcAccessStatusCommand.Add(arcAccessWalletOption);
arcAccessStatusCommand.Add(arcAccessStrategyOption);
SetAction(arcAccessStatusCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var wallet = pr.GetRequiredValue(arcAccessWalletOption);
    var strategy = pr.GetRequiredValue(arcAccessStrategyOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc access status",
            new { wallet, strategy },
            resolvedConfigPath,
            host => ArcAccessCommands.StatusAsync(CreateContext(host, options), wallet, strategy))
        .ConfigureAwait(false);
});

var arcAccessSyncCommand = new Command("sync", "Ingest a known StrategySubscribed transaction into the local access mirror");
arcAccessSyncCommand.Add(arcAccessWalletOption);
arcAccessSyncCommand.Add(arcAccessStrategyOption);
arcAccessSyncCommand.Add(arcAccessPlanIdOption);
arcAccessSyncCommand.Add(arcAccessTxHashOption);
arcAccessSyncCommand.Add(arcAccessExpiresAtOption);
arcAccessSyncCommand.Add(arcAccessBlockOption);
SetAction(arcAccessSyncCommand, async pr =>
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var wallet = pr.GetRequiredValue(arcAccessWalletOption);
    var strategy = pr.GetRequiredValue(arcAccessStrategyOption);
    var planId = pr.GetRequiredValue(arcAccessPlanIdOption);
    var txHash = pr.GetRequiredValue(arcAccessTxHashOption);
    var expiresAt = pr.GetRequiredValue(arcAccessExpiresAtOption);
    var block = pr.GetValue(arcAccessBlockOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "arc access sync",
            new { wallet, strategy, planId, txHash, expiresAt, block },
            resolvedConfigPath,
            host => ArcAccessCommands.SyncAsync(
                CreateContext(host, options),
                wallet,
                strategy,
                planId,
                txHash,
                expiresAt,
                block))
        .ConfigureAwait(false);
});

arcAccessCommand.Add(arcAccessPlansCommand);
arcAccessCommand.Add(arcAccessStatusCommand);
arcAccessCommand.Add(arcAccessSyncCommand);
arcCommand.Add(arcSignalCommand);
arcCommand.Add(arcAccessCommand);

// ============================================================================
// 注册所有命令
// ============================================================================

rootCommand.Add(runCommand);
rootCommand.Add(statusCommand);
rootCommand.Add(healthCommand);
rootCommand.Add(readinessCommand);
rootCommand.Add(liveCommand);
rootCommand.Add(strategyCommand);
rootCommand.Add(killSwitchCommand);
rootCommand.Add(positionsCommand);
rootCommand.Add(ordersCommand);
rootCommand.Add(pnlCommand);
rootCommand.Add(configCommand);
rootCommand.Add(exportCommand);
rootCommand.Add(selfImproveCommand);
rootCommand.Add(opportunityCommand);
rootCommand.Add(accountCommand);
rootCommand.Add(arcCommand);

// 默认行为：无子命令时等同于 run
SetAction(rootCommand, ExecuteRunAsync);

// ============================================================================
// 解析并执行
// ============================================================================

return await rootCommand.Parse(args)
    .InvokeAsync(new InvocationConfiguration())
    .ConfigureAwait(false);

// ============================================================================
// 辅助方法
// ============================================================================

async Task<int> ExecuteRunAsync(ParseResult pr)
{
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "run",
            new { },
            resolvedConfigPath,
            async host =>
            {
                await host.RunAsync().ConfigureAwait(false);
                return 0;
            })
        .ConfigureAwait(false);
}

async Task<int> SetStrategyEnabledAsync(ParseResult pr, bool enabled)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var id = pr.GetRequiredValue(strategyIdOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            enabled ? "strategy enable" : "strategy disable",
            new { id },
            resolvedConfigPath,
            host => StrategyCommands.SetEnabledAsync(
                CreateContext(host, options),
                id,
                enabled,
                CreateConfigService(resolvedConfigPath)))
        .ConfigureAwait(false);
}

async Task<int> SetDesiredStrategyStateAsync(
    ParseResult pr,
    Func<CommandContext, string, ConfigFileService, Task<int>> command)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var id = pr.GetRequiredValue(strategyIdOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            $"strategy {pr.CommandResult.Command.Name}",
            new { id },
            resolvedConfigPath,
            host => command(CreateContext(host, options), id, CreateConfigService(resolvedConfigPath)))
        .ConfigureAwait(false);
}

Command CreateExportCommand(
    string name,
    string description,
    bool includeMarket,
    Func<ParseResult, Task<int>> action)
{
    var command = new Command(name, description);
    command.Add(exportStrategyOption);
    if (includeMarket)
    {
        command.Add(exportMarketOption);
    }

    command.Add(exportFromOption);
    command.Add(exportToOption);
    command.Add(exportLimitOption);
    command.Add(exportOutputOption);
    SetAction(command, action);
    return command;
}

async Task<int> ExportDecisionsAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var marketId = pr.GetValue(exportMarketOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export decisions",
            new { strategyId, marketId, from, to, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportDecisionsAsync(
                CreateContext(host, options),
                strategyId,
                marketId,
                from,
                to,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportOrdersAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var marketId = pr.GetValue(exportMarketOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export orders",
            new { strategyId, marketId, from, to, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportOrdersAsync(
                CreateContext(host, options),
                strategyId,
                marketId,
                from,
                to,
                1,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportTradesAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var marketId = pr.GetValue(exportMarketOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export trades",
            new { strategyId, marketId, from, to, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportTradesAsync(
                CreateContext(host, options),
                strategyId,
                marketId,
                from,
                to,
                1,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportPnlAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export pnl",
            new { strategyId, from, to },
            resolvedConfigPath,
            host => ExportCommands.ExportPnLAsync(
                CreateContext(host, options),
                strategyId ?? string.Empty,
                from,
                to,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportOrderEventsAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var marketId = pr.GetValue(exportMarketOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export order-events",
            new { strategyId, marketId, from, to, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportOrderEventsAsync(
                CreateContext(host, options),
                strategyId,
                marketId,
                from,
                to,
                1,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportRunReportAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var sessionId = pr.GetValue(exportSessionIdOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export run-report",
            new { sessionId, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportRunReportAsync(
                CreateContext(host, options),
                sessionId,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportPromotionChecklistAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var sessionId = pr.GetValue(exportSessionIdOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export promotion-checklist",
            new { sessionId, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportPromotionChecklistAsync(
                CreateContext(host, options),
                sessionId,
                limit,
                output))
        .ConfigureAwait(false);
}

async Task<int> ExportReplayPackageAsync(ParseResult pr)
{
    var options = CreateGlobalOptions(pr);
    var resolvedConfigPath = ResolveConfigPathFromParse(pr);
    var strategyId = pr.GetValue(exportStrategyOption);
    var marketId = pr.GetValue(exportMarketOption);
    var orderId = pr.GetValue(exportOrderIdOption);
    var clientOrderId = pr.GetValue(exportClientOrderIdOption);
    var sessionId = pr.GetValue(exportReplaySessionIdOption);
    var riskEventId = pr.GetValue(exportRiskEventIdOption);
    var correlationId = pr.GetValue(exportCorrelationIdOption);
    var from = pr.GetValue(exportFromOption);
    var to = pr.GetValue(exportToOption);
    var limit = pr.GetValue(exportLimitOption);
    var output = pr.GetValue(exportOutputOption);
    return await CommandAuditService.ExecuteWithAuditAsync(
            "export replay-package",
            new { strategyId, marketId, orderId, clientOrderId, sessionId, riskEventId, correlationId, from, to, limit },
            resolvedConfigPath,
            host => ExportCommands.ExportReplayPackageAsync(
                CreateContext(host, options),
                strategyId,
                marketId,
                orderId,
                clientOrderId,
                sessionId,
                riskEventId,
                correlationId,
                from,
                to,
                limit,
                output))
        .ConfigureAwait(false);
}

GlobalOptions CreateGlobalOptions(ParseResult pr) =>
    new()
    {
        JsonOutput = pr.GetValue(jsonOption),
        NonInteractive = pr.GetValue(nonInteractiveOption),
        AutoConfirm = pr.GetValue(yesOption),
        NoColor = pr.GetValue(noColorOption)
    };

CommandContext CreateContext(IHost host, GlobalOptions options) =>
    new()
    {
        Host = host,
        Services = host.Services,
        JsonOutput = options.JsonOutput,
        GlobalOptions = options
    };

string? ResolveConfigPathFromParse(ParseResult pr) =>
    ResolveConfigPath(pr.GetValue(configOption), startupCwd, exeDir);

static void AddGlobal(Command command, Option option)
{
    option.Recursive = true;
    command.Add(option);
}

static void SetAction(Command command, Func<ParseResult, Task<int>> action)
{
    command.SetAction(async pr =>
    {
        var exitCode = await action(pr).ConfigureAwait(false);
        Environment.ExitCode = exitCode;
        return exitCode;
    });
}

static Option<T> CreateOption<T>(string name, string description) =>
    new(name)
    {
        Description = description
    };

static Option<T> CreateOptionWithDefault<T>(string name, T defaultValue, string description) =>
    new(name)
    {
        Description = description,
        DefaultValueFactory = _ => defaultValue
    };

static Option<T> CreateOptionWithAliases<T>(string[] names, T defaultValue, string description)
{
    if (names.Length == 0)
    {
        throw new ArgumentException("At least one option name is required.", nameof(names));
    }

    return new Option<T>(names[0], names.Skip(1).ToArray())
    {
        Description = description,
        DefaultValueFactory = _ => defaultValue
    };
}

static Option<T> CreateRequiredOption<T>(string name, string description) =>
    new(name)
    {
        Description = description,
        Required = true
    };

static ConfigFileService CreateConfigService(string? configPath)
{
    var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
    var basePath = Path.Combine(exeDir, "appsettings.json");
    var defaultOverrideDir = TryGetDevProjectDir(exeDir) ?? exeDir;
    var overridePath = !string.IsNullOrWhiteSpace(configPath)
        ? configPath
        : Path.Combine(defaultOverrideDir, "appsettings.local.json");
    return new ConfigFileService(basePath, overridePath);
}

static string? ResolveConfigPath(string? configPath, string startupCwd, string exeDir)
{
    if (string.IsNullOrWhiteSpace(configPath))
    {
        return null;
    }

    if (Path.IsPathRooted(configPath))
    {
        var full = Path.GetFullPath(configPath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Config file not found: '{full}'.");
        }

        return full;
    }

    var fromStartupCwd = Path.GetFullPath(Path.Combine(startupCwd, configPath));
    if (File.Exists(fromStartupCwd))
    {
        return fromStartupCwd;
    }

    var fromExeDir = Path.GetFullPath(Path.Combine(exeDir, configPath));
    if (File.Exists(fromExeDir))
    {
        return fromExeDir;
    }

    throw new FileNotFoundException(
        $"Config file not found: '{configPath}'. Tried: '{fromStartupCwd}' and '{fromExeDir}'.");
}

static string? TryGetDevProjectDir(string exeDir)
{
    // dotnet run / build 输出目录通常为：Autotrade.Cli/bin/{Configuration}/{TFM}
    // 回退 3 层可得到项目目录（包含 Autotrade.Cli.csproj）
    var projectDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
    return File.Exists(Path.Combine(projectDir, "Autotrade.Cli.csproj")) ? projectDir : null;
}
