﻿using System;
using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace Xels.Bitcoin.Features.WatchOnlyWallet.Models
{
    /// <summary>
    /// Represents a watch-only wallet to be used as an API return object.
    /// </summary>
    public class WatchOnlyWalletModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WatchOnlyWalletModel"/> class.
        /// </summary>
        public WatchOnlyWalletModel()
        {
            this.WatchedAddresses = new List<WatchedAddressModel>();
        }

        /// <summary>
        /// The network this wallet is for.
        /// </summary>
        [JsonProperty(PropertyName = "network")]
        [JsonConverter(typeof(NetworkConverter))]
        public Network Network { get; set; }

        /// <summary>
        /// The type of coin, Bitcoin or Xels.
        /// </summary>
        [JsonProperty(PropertyName = "coinType")]
        public CoinType CoinType { get; set; }

        /// <summary>
        /// The time this wallet was created.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// The list of <see cref="WatchedAddress"/>es being watched.
        /// </summary>
        [JsonProperty(PropertyName = "watchedAddresses")]
        public ICollection<WatchedAddressModel> WatchedAddresses { get; set; }

        /// <summary>
        /// The list of transactions being watched.
        /// </summary>
        [JsonProperty(PropertyName = "watchedTransactions")]
        public ICollection<WatchedTransactionModel> WatchedTransactions { get; set; }
    }

    /// <summary>
    /// An object contaning an address being watched along with any transactions affecting it.
    /// </summary>
    public class WatchedAddressModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WatchedAddressModel"/> class.
        /// </summary>
        public WatchedAddressModel()
        {
            this.Transactions = new List<TransactionVerboseModel>();
        }

        /// <summary>
        /// A base58 address being watched for transactions affecting it.
        /// </summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        /// <summary>
        /// The list of transactions affecting the address being watched.
        /// </summary>
        [JsonProperty(PropertyName = "transactions")]
        public ICollection<TransactionVerboseModel> Transactions { get; set; }
    }

    /// <summary>
    /// An object contaning a transaction being watched.
    /// </summary>
    public class WatchedTransactionModel
    {
        /// <summary>
        /// The transaction being watched.
        /// </summary>
        [JsonProperty(PropertyName = "transaction")]
        public TransactionVerboseModel Transaction { get; set; }
    }
}
