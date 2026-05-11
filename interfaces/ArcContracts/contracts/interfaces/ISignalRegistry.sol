// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface ISignalRegistry {
    struct SignalSummary {
        address agent;
        string venue;
        string strategyKey;
        bytes32 reasoningHash;
        bytes32 riskEnvelopeHash;
        int256 expectedEdgeBps;
        uint256 maxNotionalUsdc;
        uint64 validUntil;
        uint64 publishedAt;
        bool exists;
    }

    event SignalPublished(
        bytes32 indexed signalId,
        address indexed agent,
        string venue,
        string strategyKey,
        bytes32 reasoningHash,
        bytes32 riskEnvelopeHash,
        int256 expectedEdgeBps,
        uint256 maxNotionalUsdc,
        uint64 validUntil,
        uint64 publishedAt
    );

    function publishSignal(
        bytes32 signalId,
        address agent,
        string calldata venue,
        string calldata strategyKey,
        bytes32 reasoningHash,
        bytes32 riskEnvelopeHash,
        int256 expectedEdgeBps,
        uint256 maxNotionalUsdc,
        uint64 validUntil
    ) external;

    function getSignal(bytes32 signalId) external view returns (SignalSummary memory);
}
