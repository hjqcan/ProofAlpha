// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IStrategyAccess {
    struct Plan {
        bytes32 strategyKey;
        uint256 price;
        uint64 durationSeconds;
        bool active;
    }

    event PlanConfigured(
        uint256 indexed planId,
        bytes32 indexed strategyKey,
        uint256 price,
        uint64 durationSeconds,
        bool active
    );
    event StrategySubscribed(
        address indexed user,
        bytes32 indexed strategyKey,
        uint256 indexed planId,
        uint256 amount,
        uint64 expiresAt
    );

    function setPlan(
        uint256 planId,
        bytes32 strategyKey,
        uint256 price,
        uint64 durationSeconds,
        bool active
    ) external;

    function subscribe(bytes32 strategyKey, uint256 planId) external;

    function hasAccess(address user, bytes32 strategyKey) external view returns (bool);

    function accessExpiresAt(address user, bytes32 strategyKey) external view returns (uint64);
}
