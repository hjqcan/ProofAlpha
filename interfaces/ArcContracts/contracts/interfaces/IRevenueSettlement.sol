// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IRevenueSettlement {
    event SettlementRecorded(
        bytes32 indexed settlementId,
        bytes32 indexed signalId,
        address indexed token,
        uint256 grossAmount,
        address[] recipients,
        uint16[] shareBps,
        uint64 recordedAt
    );

    function recordSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external;
}
