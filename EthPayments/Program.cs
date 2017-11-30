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
                var config = new EthPaymentsConfig(wallets, "http://127.0.0.1:8545/", "");
                var paymentService = new EthPayments(config);
                
                if (args.Count() > 0)
                {
                    var fromBlock = int.Parse(args[0]);
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
