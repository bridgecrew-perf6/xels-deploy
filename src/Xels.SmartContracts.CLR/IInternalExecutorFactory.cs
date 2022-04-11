namespace Xels.SmartContracts.CLR
{
    public interface IInternalExecutorFactory
    {
        Stratis.SmartContracts.IInternalTransactionExecutor Create(RuntimeObserver.IGasMeter gasMeter, IState state);
    }
}