﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Features.Wallet.Validations;
using Xels.Bitcoin.Utilities.ValidationAttributes;

namespace Xels.Bitcoin.Features.SmartContracts.Models
{
    /// <summary>
    /// A class containing the necessary parameters to perform a smart contract method call request.
    /// </summary>
    public class BuildCallContractTransactionRequest
    {
        public BuildCallContractTransactionRequest()
        {
            this.AccountName = "account 0";
        }

        /// <summary>
        /// The name of the wallet containing funds to use to cover transaction fees, gas, and any funds specified in the
        /// Amount field.
        /// </summary>
        [Required(ErrorMessage = "The name of the wallet is missing.")]
        public string WalletName { get; set; }


        /// <summary>
        /// The name of the wallet account containing funds to use to cover transaction fees, gas, and any funds specified in the
        /// Amount field. Defaults to "account 0".
        /// </summary>
        public string AccountName { get; set; }

        /// <summary>
        /// A list of outpoints to use as inputs for the transaction.
        /// </summary> 
        public List<OutpointRequest> Outpoints { get; set; }

        /// <summary>
        /// The address of the smart contract containing the method.
        /// </summary>
        [Required(ErrorMessage = "A smart contract address is required.")]
        [IsBitcoinAddress]
        public string ContractAddress { get; set; }

        /// <summary>
        /// The name of the method to call.
        /// </summary>
        [Required(ErrorMessage = "A method name is required.")]
        public string MethodName { get; set; }

        /// <summary>
        /// The amount of STRAT (or sidechain coin) to send to the smart contract address.
        /// </summary>
        [Required(ErrorMessage = "An amount is required but this can be set to 0.")]
        public string Amount { get; set; }

        /// <summary>
        /// The fees in STRAT (or sidechain coin) to cover the method call transaction.
        /// </summary>
        [MoneyFormat(isRequired: true, ErrorMessage = "The fee is not in the correct format.")]
        public string FeeAmount { get; set; }

        /// <summary>
        /// The password for the wallet.
        /// </summary>
        [Required(ErrorMessage = "A password for the wallet is required.")]
        public string Password { get; set; }

        /// <summary>
        /// The gas price to charge when the method is run by the miner mining the call transaction. 
        /// </summary>
        [Range(SmartContractMempoolValidator.MinGasPrice, SmartContractFormatLogic.GasPriceMaximum)]
        public ulong GasPrice { get; set; }

        /// <summary>
        /// The maximum amount of gas that can be spent executing this transaction.
        /// This limit cannot be exceeded when the method is 
        /// run by the miner mining the call transaction. If the gas spent exceeds this value, 
        /// execution of the smart contract stops.
        /// </summary>
        [Range(SmartContractFormatLogic.GasLimitCallMinimum, SmartContractFormatLogic.GasLimitMaximum)]
        public ulong GasLimit { get; set; }

        /// <summary>
        /// A wallet address containing the funds to cover transaction fees, gas, and any funds specified in the
        /// Amount field. Some methods, such as a withdrawal method on an escrow smart contract,
        /// should only be executed by the deployer. In this case, it is this address that identifies
        /// the deployer.
        /// It is recommended that you use /api/SmartContractWallet/account-addresses to retrieve
        /// an address to use for smart contracts. This enables you to obtain a smart contract transaction history.
        /// However, any sender address containing the required funds will work.
        /// </summary>
        [Required(ErrorMessage = "Sender is required.")]
        [IsBitcoinAddress]
        public string Sender { get; set; }

        /// <summary>
        /// An array of encoded strings containing the parameters (and their type) to pass to the smart contract
        /// method when it is called. More information on the
        /// format of a parameter string is available
        /// <a target="_blank" href="https://academy.xelsplatform.com/SmartContracts/working-with-contracts.html#parameter-serialization">here</a>.
        /// </summary>
        public string[] Parameters { get; set; }

        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.Append(string.Format("{0}:{1},{2}:{3},", nameof(this.WalletName), this.WalletName, nameof(this.AccountName), this.AccountName));
            builder.Append(string.Format("{0}:{1},{2}:{3},", nameof(this.Password), this.Password, nameof(this.FeeAmount), this.FeeAmount));
            builder.Append(string.Format("{0}:{1},{2}:{3},", nameof(this.GasPrice), this.GasPrice, nameof(this.GasLimit), this.GasLimit));
            builder.Append(string.Format("{0}:{1},{2}:{3},", nameof(this.Sender), this.Sender, nameof(this.Parameters), this.Parameters));

            return builder.ToString();
        }
    }
}