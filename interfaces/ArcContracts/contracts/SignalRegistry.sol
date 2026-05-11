// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

contract SignalRegistry {
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

    mapping(bytes32 => SignalSummary) private _signals;

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

    error EmptySignalId();
    error DuplicateSignal(bytes32 signalId);
    error ZeroAgent();
    error EmptyVenue();

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
    ) external {
        if (signalId == bytes32(0)) {
            revert EmptySignalId();
        }

        if (_signals[signalId].exists) {
            revert DuplicateSignal(signalId);
        }

        if (agent == address(0)) {
            revert ZeroAgent();
        }

        if (bytes(venue).length == 0) {
            revert EmptyVenue();
        }

        uint64 publishedAt = uint64(block.timestamp);
        _signals[signalId] = SignalSummary({
            agent: agent,
            venue: venue,
            strategyKey: strategyKey,
            reasoningHash: reasoningHash,
            riskEnvelopeHash: riskEnvelopeHash,
            expectedEdgeBps: expectedEdgeBps,
            maxNotionalUsdc: maxNotionalUsdc,
            validUntil: validUntil,
            publishedAt: publishedAt,
            exists: true
        });

        emit SignalPublished(
            signalId,
            agent,
            venue,
            strategyKey,
            reasoningHash,
            riskEnvelopeHash,
            expectedEdgeBps,
            maxNotionalUsdc,
            validUntil,
            publishedAt
        );
    }

    function getSignal(bytes32 signalId) external view returns (SignalSummary memory) {
        return _signals[signalId];
    }
}
