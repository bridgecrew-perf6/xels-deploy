using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using NBitcoin;

using XelsXLCDesktopWalletApp.Models.SmartContractModels;

namespace XelsXLCDesktopWalletApp.Models
{
    public class TransactionItemModel
    { 
        private string _timeStamp;
        private double _amount;

        public string Type { get; set; }
        public string TransactionType { get; set; }
        public string TransactionIcon { get; set; }
        public string ToAddress { get; set; }
        public double Amount {

            get {
                return (this._amount / 100000000);
            }
            set {
                this._amount = value;
            }
        }
        public string AmountWithUnit { get { return this.Amount.ToString()+" " + GlobalPropertyModel.CoinUnit; }   }
        public string Id { get; set; }

        public string Timestamp 
        { 
            get { return _timeStamp; } 
            set {
                _timeStamp = Utils.UnixTimeToDateTime(Convert.ToInt64(value)).ToString("F");
                    //new DateTime(Convert.ToInt32(value), System.DateTimeKind.Utc ).ToString(); //ConvertToDate(value);
            }
            //
            //DateTime.ParseExact(value,
            //                    "yyyyMMdd",
            //                    CultureInfo.InvariantCulture,
            //                    DateTimeStyles.None).ToString("d"); 
        }

        public int? ConfirmedInBlock { get; set; }
        public int? BlockIndex { get; set; }
        public decimal Fee { get; set; }         

        public List<PaymentDetailModel> Payments { get; set; } // new added for detail 
        public long TxOutputTime { get; set; } // new added for detail 
        public int TxOutputIndex { get; set; } // new added for detail 
    }
    public class PaymentDetailModel
    {
        public string DestinationAddress { get; set; }
        public double Amount { get; set; }
        public bool IsChange { get; set; }
    }

    public class TransactionItemModelArray
    {
        public TransactionItemModel[] Transactions { get; set; }
    }

    public enum TransactionItemType
    {
        Received,
        Send,
        Staked,
        Mined
    }


}
