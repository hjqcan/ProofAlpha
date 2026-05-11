// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IPerformanceLedger {
    enum OutcomeStatus {
        Unknown,
        Executed,
        Rejected,
        Expired,
        Skipped,
        Failed,
        Revoked
    }

    struct OutcomeSummary {
        OutcomeStatus status;
        int256 realizedPnlBps;
        int256 slippageBps;
        bytes32 outcomeHash;
        address recorder;
        uint64 recordedAt;
        bool exists;
    }

    event OutcomeRecorded(
        bytes32 indexed signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash,
        address indexed recorder,
        uint64 recordedAt
    );

    function recordOutcome(
        bytes32 signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash
    ) external;

    function getOutcome(bytes32 signalId) external view returns (OutcomeSummary memory);
}
