﻿using NBitcoin;

namespace Xels.SmartContracts.CLR
{
    /// <summary>
    /// Represents an internal contract creation message. Occurs when a contract creates another contract
    /// using its <see cref="SmartContract.Create{T}"/> method.
    /// </summary>
    public class InternalCreateMessage : BaseMessage
    {
        public InternalCreateMessage(uint160 from, ulong amount, RuntimeObserver.Gas gasLimit, object[] parameters, string typeName)
            : base(from, amount, gasLimit)
        {
            this.Parameters = parameters;
            this.Type = typeName;
        }

        /// <summary>
        /// The parameters to use when creating the contract.
        /// </summary>
        public object[] Parameters{ get; }

        /// <summary>
        /// The Type of contract to create.
        /// </summary>
        public string Type { get; }
    }
}