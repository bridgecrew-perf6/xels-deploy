﻿using System.ComponentModel.DataAnnotations;
using Xels.SmartContracts.CLR.Serialization;

namespace Xels.Bitcoin.Features.SmartContracts.Models
{
    /// <summary>
    /// A class containing the necessary parameters to perform a retrieve stored data request.
    /// </summary>
    public class GetStorageRequest
    {
        /// <summary>
        /// The height at which to query the state. If unset, will default to the current chain tip.
        /// </summary>
        public ulong? BlockHeight { get; set; }

        /// <summary>
        /// The address of the smart contract.
        /// </summary>
        [Required(ErrorMessage = "A smart contract address is required.")]
        public string ContractAddress { get; set; }

        /// <summary>
        /// The key for the piece of stored data to retrieve.
        /// </summary>
        [Required(ErrorMessage = "A key for the stored data is required.")]
        public string StorageKey { get; set; }

        /// <summary>
        /// The stored data type.
        /// </summary>
        [Required(ErrorMessage = "The type of the data is required.")]
        public MethodParameterDataType DataType { get; set; }
    }
}