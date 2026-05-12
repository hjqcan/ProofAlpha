// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IERC20Like {
    function transferFrom(address from, address to, uint256 amount) external returns (bool);
}

contract StrategyAccess {
    struct Plan {
        bytes32 strategyKey;
        uint256 price;
        uint64 durationSeconds;
        bool active;
    }

    IERC20Like public immutable paymentToken;
    address public immutable treasury;
    address public owner;

    mapping(uint256 => Plan) public plans;
    mapping(address => mapping(bytes32 => uint64)) private _accessExpiresAt;

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

    error NotOwner();
    error ZeroAddress();
    error InvalidPlan();
    error InactivePlan();
    error StrategyPlanMismatch();
    error PaymentFailed();

    constructor(address paymentTokenAddress, address treasuryAddress) {
        if (paymentTokenAddress == address(0) || treasuryAddress == address(0)) {
            revert ZeroAddress();
        }

        paymentToken = IERC20Like(paymentTokenAddress);
        treasury = treasuryAddress;
        owner = msg.sender;
    }

    modifier onlyOwner() {
        if (msg.sender != owner) {
            revert NotOwner();
        }

        _;
    }

    function setPlan(
        uint256 planId,
        bytes32 strategyKey,
        uint256 price,
        uint64 durationSeconds,
        bool active
    ) external onlyOwner {
        if (planId == 0 || strategyKey == bytes32(0) || price == 0 || durationSeconds == 0) {
            revert InvalidPlan();
        }

        plans[planId] = Plan({
            strategyKey: strategyKey,
            price: price,
            durationSeconds: durationSeconds,
            active: active
        });

        emit PlanConfigured(planId, strategyKey, price, durationSeconds, active);
    }

    function subscribe(bytes32 strategyKey, uint256 planId) external {
        Plan memory plan = plans[planId];
        if (plan.durationSeconds == 0) {
            revert InvalidPlan();
        }

        if (!plan.active) {
            revert InactivePlan();
        }

        if (plan.strategyKey != strategyKey) {
            revert StrategyPlanMismatch();
        }

        if (!paymentToken.transferFrom(msg.sender, treasury, plan.price)) {
            revert PaymentFailed();
        }

        uint64 currentExpiry = _accessExpiresAt[msg.sender][strategyKey];
        uint64 startAt = currentExpiry > block.timestamp ? currentExpiry : uint64(block.timestamp);
        uint64 expiresAt = startAt + plan.durationSeconds;
        _accessExpiresAt[msg.sender][strategyKey] = expiresAt;

        emit StrategySubscribed(msg.sender, strategyKey, planId, plan.price, expiresAt);
    }

    function hasAccess(address user, bytes32 strategyKey) external view returns (bool) {
        return _accessExpiresAt[user][strategyKey] > block.timestamp;
    }

    function accessExpiresAt(address user, bytes32 strategyKey) external view returns (uint64) {
        return _accessExpiresAt[user][strategyKey];
    }
}
