// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IERC20Payout {
    function transfer(address to, uint256 amount) external returns (bool);
}

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
    event SettlementDistributed(
        bytes32 indexed settlementId,
        address indexed token,
        address[] recipients,
        uint256[] amounts
    );

    error EmptySettlementId();
    error EmptySignalId();
    error ZeroToken();
    error ZeroGrossAmount();
    error DuplicateSettlement(bytes32 settlementId);
    error ShareLengthMismatch();
    error InvalidShareBps();
    error TransferFailed(address recipient, uint256 amount);

    function recordSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external {
        _recordSettlement(settlementId, signalId, token, grossAmount, recipients, shareBps);
    }

    function recordAndDistributeSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) external {
        _recordSettlement(settlementId, signalId, token, grossAmount, recipients, shareBps);

        IERC20Payout payoutToken = IERC20Payout(token);
        uint256[] memory amounts = new uint256[](recipients.length);
        uint256 remaining = grossAmount;

        for (uint256 index = 0; index < recipients.length; index++) {
            uint256 amount = index == recipients.length - 1
                ? remaining
                : (grossAmount * shareBps[index]) / 10_000;
            remaining -= amount;
            amounts[index] = amount;

            if (!payoutToken.transfer(recipients[index], amount)) {
                revert TransferFailed(recipients[index], amount);
            }
        }

        emit SettlementDistributed(settlementId, token, recipients, amounts);
    }

    function _recordSettlement(
        bytes32 settlementId,
        bytes32 signalId,
        address token,
        uint256 grossAmount,
        address[] calldata recipients,
        uint16[] calldata shareBps
    ) private {
        if (settlementId == bytes32(0)) {
            revert EmptySettlementId();
        }

        if (signalId == bytes32(0)) {
            revert EmptySignalId();
        }

        if (token == address(0)) {
            revert ZeroToken();
        }

        if (grossAmount == 0) {
            revert ZeroGrossAmount();
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
