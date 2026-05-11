# AGENTS.md

## Project overview

- C# (.NET 10) Polymarket auto-trading system using DDD bounded contexts.
- Contexts: Trading, MarketData, Strategy; CLI host in `Autotrade.Cli`.
- Shared infrastructure in `Shared/` (EF Core base context, event bus, Polymarket client).

## Setup commands

- Restore: `dotnet restore`
- Build: `dotnet build`
- Test: `dotnet test`

## Testing notes

- Optional Postgres smoke tests require `AUTOTRADE_TEST_POSTGRES` environment variable.
- Warnings are treated as errors (see `Directory.Build.props`).

## Configuration

- Use `Autotrade.Cli/appsettings.json` plus environment variables or user secrets.
- Do not commit real API keys or private keys.

## Code conventions

- Nullable reference types enabled.
- Prefer bounded-context isolation; avoid cross-context references unless explicitly shared.
- Use `BaseDbContext.Commit()` for unit-of-work semantics; direct `SaveChanges` is supported but keep domain events in mind.

## References

- AGENTS.md format: https://agents.md

## 不要留任何技术债务

不要为了逃避问题而选择简化实现/折中方案，不要留任务技术债务