﻿using System;
using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace Xels.Features.FederatedPeg.Wallet
{
    public class SpendingDetails
    {
        public SpendingDetails()
        {
            this.Payments = new List<PaymentDetails>();
        }

        /// <summary>
        /// The id of the transaction in which the output referenced in this transaction is spent.
        /// </summary>
        [JsonProperty(PropertyName = "transactionId", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }

        /// <summary>
        /// A list of payments made out in this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "payments", NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<PaymentDetails> Payments { get; set; }

        /// <summary>
        /// The height of the block including this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "blockHeight", NullValueHandling = NullValueHandling.Ignore)]
        public int? BlockHeight { get; set; }

        /// <summary>
        /// The hash of the block including this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "blockHash", NullValueHandling = NullValueHandling.Ignore)]
        public uint256 BlockHash { get; set; }

        /// <summary>
        /// A value indicating whether this is a coin stake transaction or not.
        /// </summary>
        [JsonProperty(PropertyName = "isCoinStake", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsCoinStake { get; set; }

        /// <summary>
        /// Gets or sets the creation time.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// Spending transaction.
        /// </summary>
        [JsonIgnore]
        public Transaction Transaction { get; set; }

        /// <summary>
        /// If this spending transaction is a withdrawal, this contains its details.
        /// </summary>
        [JsonProperty(PropertyName = "withdrawalDetails", NullValueHandling = NullValueHandling.Ignore)]
        public WithdrawalDetails WithdrawalDetails { get; set; }

        /// <summary>
        /// Determines whether this transaction being spent is confirmed.
        /// </summary>
        /// <returns><c>True</c> if the transaction is confirmed and <c>false</c> otherwise.</returns>
        public bool IsSpentConfirmed()
        {
            return this.BlockHeight != null;
        }
    }
}