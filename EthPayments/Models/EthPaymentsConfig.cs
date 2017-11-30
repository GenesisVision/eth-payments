using System.Collections.Generic;
using System.Linq;

namespace EthPayments.Models
{
    public class EthPaymentsConfig
    {
        private EthPaymentsConfig()
        {

        }

        public EthPaymentsConfig(IEnumerable<string> wallets, string gethAddress, string callbackUrl)
        {
            Wallets = wallets.Select(w => w.ToLower()).ToList();
            WalletsTrimmed = Wallets.Select(x => x.Substring(2, 40)).ToList();
            GethAddress = gethAddress;
            CallbackUrl = callbackUrl;
        }

        public List<string> Wallets { get; set; }
        public List<string> WalletsTrimmed { get; set; }
        public string GethAddress { get; set; }
        public string CallbackUrl { get; set; }
    }
}
