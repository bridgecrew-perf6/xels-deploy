using Xels.SmartContracts.Core.State;

namespace Xels.SmartContracts.CLR
{
    public interface IVirtualMachine
    {
        VmExecutionResult Create(IStateRepository repository,
            Stratis.SmartContracts.ISmartContractState contractState,
            ExecutionContext executionContext,
            byte[] contractCode,
            object[] parameters,
            string typeName = null);

        VmExecutionResult ExecuteMethod(Stratis.SmartContracts.ISmartContractState contractState,
            ExecutionContext executionContext,
            MethodCall methodCall,
            byte[] contractCode,
            string typeName);
    }
}
