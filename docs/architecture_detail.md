Architecture Design Document
Overview

This document describes the architecture of a Polymarket auto‑trading bot implemented in C# using Domain‑Driven Design (DDD).
The goal is to create an extensible, high‑performance system that can implement multiple trading strategies—such as two‑leg arbitrage, high‑probability tail sweeps, short‑term volatility trading and liquidity provision—while ensuring robust risk management and maintainability.

Design Principles

Domain‑Driven Design (DDD): Separate business logic from infrastructure concerns, and model the core concepts (markets, orders, positions, strategies) as explicit domain objects.

Modularity: Break the system into bounded contexts: Trading, Market Data, Strategy Execution, Infrastructure, and Application. Each context encapsulates its own entities, services and repositories.

Extensibility: Use interfaces and strategy patterns to allow new trading strategies or data providers to be added without impacting existing code.

Concurrency and Performance: Use asynchronous I/O and WebSocket subscriptions to handle real‑time market data and order execution with minimal latency.

Risk Control: Provide a dedicated risk management service to monitor exposures, limit order sizes, and enforce stop‑loss rules.

System Architecture

1. Trading Domain

This bounded context encapsulates accounts, positions and order management.

Component Responsibility
Account Entity Represents a Polymarket account, holding balances, positions and order history.
Position Entity Records exposure in a specific market (e.g. YES/NO on a market), including quantity, average cost and realized PnL.
Trade Aggregate Aggregates Account and Position; enforces transactional invariants for placing or cancelling orders.
OrderService Domain service that issues new orders, cancels existing ones and updates positions. Emits domain events (e.g. OrderPlaced, OrderFilled).
TradeRepository Interface providing persistence for accounts, positions and orders; implemented by the infrastructure layer.

2. Market Data Domain

Responsible for discovering available markets and maintaining real‑time data.

Component Responsibility
Market Entity Represents a Polymarket market (ID, name, category, expiry).
OrderBook Entity Maintains live bid/ask levels for a market; updated via WebSocket.
MarketDataService Manages subscriptions to Polymarket’s WebSocket feeds, handles reconnections, and publishes updates to interested strategies.
MarketRepository Caches market information and provides filtering (e.g. by expiry, category).

3. Strategy Execution Domain

Encapsulates trading algorithms and their configuration.

Component Responsibility
ITradingStrategy Core interface defining methods like SelectMarkets(), EvaluateEntry(), EvaluateExit() and OnOrderFilled(). All strategies implement this interface.
DualLegArbitrageStrategy Implements the two‑leg arbitrage: buys one side when cheap, waits for the opposite side to become cheap, and ensures total cost < 1 USD. Includes logic to track average costs and positions.
EndgameSweepStrategy Implements high‑probability tail sweeps: detects markets nearing resolution with YES/NO probabilities above 90%, and buys the winning side to lock in small but almost certain profits.
LiquidityMakingStrategy Posts bids and offers on illiquid markets to capture spread and earn liquidity rewards. Maintains delta neutrality by adjusting orders on both sides.
VolatilityScalpingStrategy Optional strategy focusing on very short‑term mispricings in 15‑minute markets (e.g. Up/Down markets). Looks for price oscillations and enters/exits within minutes.
StrategyManager Coordinates multiple strategies, allocates capital across them, starts/stops strategies and aggregates performance metrics.
RiskManagementService Monitors per‑strategy and per‑market exposure, sets maximum position limits, enforces stop‑loss or stop‑out rules, and can halt the system if risk parameters are exceeded.

4. Application Layer

Provides façade and orchestration for the domain services. It exposes commands for starting/stopping strategies, retrieving account status, adjusting parameters and responding to domain events.

TradingAppService: Entry point for external callers (CLI, UI, REST). It wires up the StrategyManager, RiskManagementService and domain services.

DTOs & Mappers: Transforms domain models to/from data transfer objects used by the UI or API.

CQRS: Optionally separates reads (queries) from writes (commands) to optimize performance; read models may be cached in memory for fast access.

5. Infrastructure Layer

This layer bridges the domain model to external systems such as Polymarket APIs, databases and message queues.

PolymarketApiClient: Encapsulates HTTP calls (for placing/cancelling orders and retrieving market lists) and WebSocket subscriptions (for order books and account fills). Implements retry logic and rate‑limiting.

Repository Implementations: Concrete classes for TradeRepository and MarketRepository, using persistence technologies like SQLite or PostgreSQL to store positions and orders, plus in‑memory caches for high‑speed access.

Logging & Metrics: Uses .NET logging frameworks (Serilog, NLog) to record every event and uses Prometheus or similar to expose metrics on latency, order fill times, PnL and risk measures.

Configuration & Dependency Injection: Stores secrets (API keys, private keys) securely (e.g. environment variables, secret store). Uses .NET’s built‑in DI container to wire up services.

6. User Interface (Optional)

Two main interfaces can interact with the application layer:

Command‑Line Interface (CLI) – Allows traders or engineers to start/stop strategies, configure parameters and view real‑time positions/logs via the terminal.

Web or Desktop GUI – Built with Blazor, WPF or other frameworks, it offers dashboards showing market lists, positions, orders, PnL curves and risk metrics. It also allows toggling strategies and adjusting parameters at runtime.

Sequence of Operations

Startup: The application initializes configuration, repositories, API client, strategy instances and risk management. It connects to Polymarket’s WebSocket for account events and order book updates.

Market Discovery: MarketDataService retrieves available markets and notifies strategies about those matching their criteria (e.g. 15‑minute markets).

Strategy Loop:

Each strategy selects markets via SelectMarkets().

For each subscribed market, strategies evaluate entry conditions (price thresholds, pair cost calculations) and call OrderService to place orders via the API client.

Upon order fill, OrderService updates positions and triggers OnOrderFilled() in the strategy, allowing it to proceed to the next leg or update risk metrics.

Risk Monitoring: RiskManagementService continuously checks exposures. If thresholds are breached, it instructs the StrategyManager to reduce positions or suspend strategies.

Termination: When stopping, the StrategyManager cancels outstanding orders, exits positions if configured, and persists final state for later resumption.

Key Considerations

Latency: For short‑term markets, network latency is crucial. Deploy the bot on a VPS geographically close to Polymarket’s infrastructure to minimize round‑trip time.

Fault Tolerance: Implement retries and exponential backoff for API failures; persist positions and orders so that the system can recover after a crash without losing state.

Security: Store private keys securely; never log sensitive credentials; consider hardware wallets or offline signing where possible.

Compliance: Ensure the bot complies with Polymarket’s terms of service and local regulations, especially regarding KYC/AML and geographic restrictions.

Extensibility: Design new strategy classes by implementing ITradingStrategy, enabling future strategies such as machine‑learning based predictors or cross‑exchange arbitrage.
