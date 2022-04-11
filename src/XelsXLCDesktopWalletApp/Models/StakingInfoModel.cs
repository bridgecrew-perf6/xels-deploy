using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models
{
    public class StakingInfoModel 
    {
        public bool Enabled { get; set; }
        public bool Staking { get; set; }
        public string Errors { get; set; }
        public long CurrentBlockSize { get; set; }
        public long CurrentBlockTx { get; set; }
        public long PooledTx { get; set; }
        public double Difficulty { get; set; }
        public int SearchInterval { get; set; }
        public long Weight { get; set; }
        public long NetStakeWeight { get; set; }
        public long Immature { get; set; }
        public long ExpectedTime { get; set; }
    }
}
