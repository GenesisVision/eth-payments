using EthPayments.Models;
using NLog;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EthPayments
{
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                logger.Info("EthPayments started");

                var wallets = File.ReadAllLines("wallets.txt");
                var gethAddress = "http://127.0.0.1:8545/";
                var callbackUrl = "";
                var contractAddress = "";
                var config = new EthPaymentsConfig(wallets, gethAddress, callbackUrl, contractAddress);

                IPayments paymentService;
                switch (args.FirstOrDefault())
                {
                    case "-eth":
                        paymentService = new EthPayments(config);
                        break;
                    case "-token":
                        paymentService = new TokenPayment(config);
                        break;
                    default:
                        throw new ArgumentNullException();
                }
                
                if (args.Count() > 1)
                {
                    var fromBlock = long.Parse(args[1]);
                    logger.Info($"From block: {fromBlock}");
                    paymentService.VerifyWalletsAsync(fromBlock).GetAwaiter().GetResult();
                }

                while (true)
                {
                    try
                    {
                        paymentService.VerifyWalletsAsync().GetAwaiter().GetResult();
                        Thread.Sleep(1 * 1000);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex.ToString());                        
                        Thread.Sleep(30 * 1000);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Fatal(ex.ToString());
                Console.ReadKey();
            }
        }        
    }
}
