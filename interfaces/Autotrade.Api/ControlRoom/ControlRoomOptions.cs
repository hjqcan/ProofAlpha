namespace Autotrade.Api.ControlRoom;

public sealed class ControlRoomOptions
{
    public const string SectionName = "AutotradeApi:ControlRoom";

    public string DataMode { get; set; } = "LocalOnly";

    public string CommandMode { get; set; } = ControlRoomCommandModes.ReadOnly;

    public decimal PaperCapital { get; set; } = 10_000m;

    public bool EnableControlCommands { get; set; }

    public bool RequireLocalAccess { get; set; } = true;

    public bool EnablePublicMarketData { get; set; } = true;

    public int MarketLimit { get; set; } = 40;

    public int MarketDiscoveryPageSize { get; set; } = 500;

    public int OrderBookLevels { get; set; } = 12;

    public int MarketCacheTtlSeconds { get; set; } = 60;

    public int OrderBookCacheTtlSeconds { get; set; } = 5;

    public int OrderBookFreshSeconds { get; set; } = 5;

    public int OrderBookStaleSeconds { get; set; } = 30;

    public string EffectiveCommandMode => EnableControlCommands
        ? ControlRoomCommandModes.Normalize(CommandMode)
        : ControlRoomCommandModes.ReadOnly;

    public bool AllowsControlCommands => EnableControlCommands
        && ControlRoomCommandModes.AllowsCommands(CommandMode);
}

public static class ControlRoomCommandModes
{
    public const string ReadOnly = "ReadOnly";

    public const string Paper = "Paper";

    public const string LiveServices = "LiveServices";

    public static string Normalize(string? commandMode)
    {
        if (string.Equals(commandMode, Paper, StringComparison.OrdinalIgnoreCase))
        {
            return Paper;
        }

        if (string.Equals(commandMode, LiveServices, StringComparison.OrdinalIgnoreCase))
        {
            return LiveServices;
        }

        return ReadOnly;
    }

    public static bool AllowsCommands(string? commandMode)
    {
        var normalized = Normalize(commandMode);
        return normalized is Paper or LiveServices;
    }
}
