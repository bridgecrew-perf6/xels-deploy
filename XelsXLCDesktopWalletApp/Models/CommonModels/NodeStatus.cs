using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models.CommonModels
{
    public class NodeStatusModel
    {
         
        public NodeStatusModel()
        {
            this.InboundPeers = new List<ConnectedPeerModel>();
            this.OutboundPeers = new List<ConnectedPeerModel>();
            this.FeaturesData = new List<FeatureData>();
        }
 
        public string Agent { get; set; }
 
        public string Version { get; set; }

      
        public string ExternalAddress { get; set; }
 
        public string Network { get; set; }
 
        public string CoinTicker { get; set; }

 
        public int ProcessId { get; set; }
        
        public int? ConsensusHeight { get; set; }
         
        public int BlockStoreHeight { get; set; }
 
        public int? BestPeerHeight { get; set; }
         
        public List<ConnectedPeerModel> InboundPeers { get; set; }
         
        public List<ConnectedPeerModel> OutboundPeers { get; set; }
         
        public List<FeatureData> FeaturesData { get; set; }
         
        public string DataDirectoryPath { get; set; }
         
        public TimeSpan RunningTime { get; set; }
         
        public double Difficulty { get; set; }
         
        public uint ProtocolVersion { get; set; }
 
        public bool Testnet { get; set; }

         
        public decimal RelayFee { get; set; }
         
        public string State { get; set; }
         
        public bool? InIbd { get; set; }
    }

     
    public class FeatureData
    {
     
        public string Namespace { get; set; }
 
        public string State { get; set; }
    }

    public class ConnectedPeerModel
    { 
        public bool IsInbound { get; set; }
         
        public string Version { get; set; }
         
        public string RemoteSocketEndpoint { get; set; } 
        public int TipHeight { get; set; }
    }
}
