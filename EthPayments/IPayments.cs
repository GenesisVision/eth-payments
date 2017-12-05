using System.Threading.Tasks;

namespace EthPayments
{
    public interface IPayments
    {
        Task VerifyWalletsAsync(long? fromBlock = null);
    }
}