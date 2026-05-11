namespace Autotrade.Api.Models;

public sealed record ApiHealthResponse(
    string Status,
    string Application,
    string Environment,
    DateTimeOffset TimestampUtc,
    string Version);

public sealed record ApiDetailedHealthResponse(
    string Status,
    string Application,
    string Environment,
    DateTimeOffset TimestampUtc,
    string Version,
    IReadOnlyList<ApiHealthEntryResponse> Entries);

public sealed record ApiHealthEntryResponse(
    string Name,
    string Status,
    string? Description,
    TimeSpan Duration);
