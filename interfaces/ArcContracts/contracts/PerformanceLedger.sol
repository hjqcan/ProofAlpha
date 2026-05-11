// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

contract PerformanceLedger {
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

    mapping(bytes32 => OutcomeSummary) private _outcomes;

    event OutcomeRecorded(
        bytes32 indexed signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash,
        address indexed recorder,
        uint64 recordedAt
    );
    event OutcomeCorrected(
        bytes32 indexed signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash,
        bytes32 correctionHash,
        address indexed recorder,
        uint64 correctedAt
    );

    error EmptySignalId();
    error InvalidOutcomeStatus();
    error TerminalOutcomeAlreadyRecorded(bytes32 signalId);
    error MissingTerminalOutcome(bytes32 signalId);

    function recordOutcome(
        bytes32 signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash
    ) external {
        if (signalId == bytes32(0)) {
            revert EmptySignalId();
        }

        if (status == OutcomeStatus.Unknown) {
            revert InvalidOutcomeStatus();
        }

        if (_outcomes[signalId].exists) {
            revert TerminalOutcomeAlreadyRecorded(signalId);
        }

        uint64 recordedAt = uint64(block.timestamp);
        _outcomes[signalId] = OutcomeSummary({
            status: status,
            realizedPnlBps: realizedPnlBps,
            slippageBps: slippageBps,
            outcomeHash: outcomeHash,
            recorder: msg.sender,
            recordedAt: recordedAt,
            exists: true
        });

        emit OutcomeRecorded(signalId, status, realizedPnlBps, slippageBps, outcomeHash, msg.sender, recordedAt);
    }

    function correctOutcome(
        bytes32 signalId,
        OutcomeStatus status,
        int256 realizedPnlBps,
        int256 slippageBps,
        bytes32 outcomeHash,
        bytes32 correctionHash
    ) external {
        if (!_outcomes[signalId].exists) {
            revert MissingTerminalOutcome(signalId);
        }

        if (status == OutcomeStatus.Unknown || correctionHash == bytes32(0)) {
            revert InvalidOutcomeStatus();
        }

        uint64 correctedAt = uint64(block.timestamp);
        _outcomes[signalId] = OutcomeSummary({
            status: status,
            realizedPnlBps: realizedPnlBps,
            slippageBps: slippageBps,
            outcomeHash: outcomeHash,
            recorder: msg.sender,
            recordedAt: correctedAt,
            exists: true
        });

        emit OutcomeCorrected(
            signalId,
            status,
            realizedPnlBps,
            slippageBps,
            outcomeHash,
            correctionHash,
            msg.sender,
            correctedAt
        );
    }

    function getOutcome(bytes32 signalId) external view returns (OutcomeSummary memory) {
        return _outcomes[signalId];
    }
}
