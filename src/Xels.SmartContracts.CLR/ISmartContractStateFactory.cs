using NBitcoin;
using Xels.SmartContracts.Core.State;

namespace Xels.SmartContracts.CLR
{
    public interface ISmartContractStateFactory
    {
        /// <summary>
        /// Sets up a new <see cref="Stratis.SmartContracts.ISmartContractState"/> based on the current state.
        /// </summary>        
        Stratis.SmartContracts.ISmartContractState Create(IState state, RuntimeObserver.IGasMeter gasMeter, uint160 address, BaseMessage message,
            IStateRepository repository);
    }
}