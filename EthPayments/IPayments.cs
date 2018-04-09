using System.Threading.Tasks;

namespace EthPayments
{
    public interface IPayments
    {
        Task<long> VerifyWalletsAsync(long? fromBlock = null);
    }
}