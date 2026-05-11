// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

contract RevenueSettlement {
    mapping(bytes32 => bool) public settlementRecorded;

    event SettlementRecorded(
        bytes32 indexed settlementId,
        bytes32 indexed signalId,
        address indexed token,
        uint256 grossAmount,
        address[] recipients,
        uint16[] shareBps,
        uint64 recordedAt
    );

    error EmptySettlementId();
    error DuplicateSettlement(bytes32 settlementId);
    error ShareLengthMismatch();
    error InvalidShareBps();

    function recordSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external {
        if (settlementId == bytes32(0)) {
            revert EmptySettlementId();
        }

        if (settlementRecorded[settlementId]) {
            revert DuplicateSettlement(settlementId);
        }

        if (recipients.length == 0 || recipients.length != shareBps.length) {
            revert ShareLengthMismatch();
        }

        uint256 totalBps = 0;
        for (uint256 index = 0; index < shareBps.length; index++) {
            if (recipients[index] == address(0) || shareBps[index] == 0) {
                revert InvalidShareBps();
            }

            totalBps += shareBps[index];
        }

        if (totalBps != 10_000) {
            revert InvalidShareBps();
        }

        settlementRecorded[settlementId] = true;
        emit SettlementRecorded(
            settlementId,
            signalId,
            token,
            grossAmount,
            recipients,
            shareBps,
            uint64(block.timestamp)
        );
    }
}
