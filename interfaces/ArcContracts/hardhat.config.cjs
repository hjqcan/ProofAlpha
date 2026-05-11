require("@nomicfoundation/hardhat-ethers");

const networks = {
  hardhat: {
    chainId: 31337
  },
  localhost: {
    url: process.env.LOCAL_RPC_URL || "http://127.0.0.1:8545"
  }
};

if (process.env.ARC_TESTNET_RPC_URL) {
  networks.arcTestnet = {
    url: process.env.ARC_TESTNET_RPC_URL,
    chainId: process.env.ARC_TESTNET_CHAIN_ID ? Number(process.env.ARC_TESTNET_CHAIN_ID) : undefined,
    accounts: process.env.ARC_SETTLEMENT_PRIVATE_KEY ? [process.env.ARC_SETTLEMENT_PRIVATE_KEY] : []
  };
}

module.exports = {
  solidity: {
    version: "0.8.24",
    settings: {
      optimizer: {
        enabled: true,
        runs: 200
      }
    }
  },
  networks
};
