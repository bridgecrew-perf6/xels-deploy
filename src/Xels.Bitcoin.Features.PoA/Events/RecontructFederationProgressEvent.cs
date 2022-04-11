using Xels.Bitcoin.EventBus;

namespace Xels.Bitcoin.Features.PoA.Events
{
    public sealed class RecontructFederationProgressEvent : EventBase
    {
        public string Progress { get; set; }
    }
}
