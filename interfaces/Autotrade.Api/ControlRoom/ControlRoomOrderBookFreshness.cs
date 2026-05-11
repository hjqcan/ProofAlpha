namespace Autotrade.Api.ControlRoom;

public static class ControlRoomOrderBookFreshness
{
    public const string Fresh = "Fresh";

    public const string Delayed = "Delayed";

    public const string Stale = "Stale";

    public static ControlRoomOrderBookFreshnessDto Evaluate(
        DateTimeOffset lastUpdatedUtc,
        DateTimeOffset observedAtUtc,
        int freshSeconds,
        int staleSeconds)
    {
        var normalizedFreshSeconds = Math.Clamp(freshSeconds, 1, 3_600);
        var normalizedStaleSeconds = Math.Clamp(
            staleSeconds,
            normalizedFreshSeconds + 1,
            86_400);
        var ageSeconds = Math.Max(0, (int)Math.Ceiling((observedAtUtc - lastUpdatedUtc).TotalSeconds));

        if (ageSeconds <= normalizedFreshSeconds)
        {
            return new ControlRoomOrderBookFreshnessDto(
                Fresh,
                ageSeconds,
                normalizedFreshSeconds,
                normalizedStaleSeconds,
                $"Order book updated {ageSeconds}s ago.");
        }

        if (ageSeconds <= normalizedStaleSeconds)
        {
            return new ControlRoomOrderBookFreshnessDto(
                Delayed,
                ageSeconds,
                normalizedFreshSeconds,
                normalizedStaleSeconds,
                $"Order book is delayed by {ageSeconds}s.");
        }

        return new ControlRoomOrderBookFreshnessDto(
            Stale,
            ageSeconds,
            normalizedFreshSeconds,
            normalizedStaleSeconds,
            $"Order book is stale by {ageSeconds}s; do not treat it as live.");
    }
}
