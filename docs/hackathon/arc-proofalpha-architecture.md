# ProofAlpha Arc Hackathon Architecture

```mermaid
flowchart LR
    OD["OpportunityDiscovery"]
    SI["SelfImprove"]
    STR["Strategy"]
    TRD["Trading"]
    CLOB["Polymarket CLOB"]
    BA["Builder attribution"]
    ARC["ArcSettlement"]
    SIG["SignalRegistry"]
    ACC["StrategyAccess"]
    PERF["PerformanceLedger"]
    REV["RevenueSettlement"]
    API["Autotrade.Api"]
    UI["Control Room / Subscriber Portal"]

    OD -->|"candidate opportunity"| STR
    SI -->|"strategy package and policy"| STR
    STR -->|"pre-execution signal and risk envelope"| ARC
    ARC --> SIG
    ARC --> ACC
    ARC --> PERF
    ARC --> REV

    ACC -->|"entitlement mirror"| API
    SIG -->|"published signal proof"| API
    PERF -->|"outcome and reputation read model"| API
    REV -->|"settlement journal"| API
    API --> UI

    UI -->|"Paper command request"| TRD
    STR -->|"decision"| TRD
    TRD -->|"Paper/live-gated order intent"| CLOB
    TRD -->|"redacted signed-order envelope"| BA
    BA -->|"arcSignalId and builderCodeHash"| ARC
    TRD -->|"execution outcome"| PERF
    ACC -->|"subscription tx hash"| REV
```

## Boundary Notes

- `OpportunityDiscovery`, `SelfImprove`, `Strategy`, and `Trading` stay in their
  bounded contexts.
- `ArcSettlement` owns proof, access, performance, and revenue contracts plus
  local journals.
- `Autotrade.Api` exposes read models and command endpoints to the Control Room.
- Polymarket remains the venue; Arc records proof and settlement evidence around
  the agent product.
- Paper execution is the default demo path. Live execution remains separately
  armed and risk-gated.
