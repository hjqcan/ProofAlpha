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

    event SettlementDistributed(
        bytes32 indexed settlementId,
        address indexed token,
        address[] recipients,
        uint256[] amounts
    );

    function recordSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external;

    function recordAndDistributeSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external;
}
