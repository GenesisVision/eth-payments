using EthPayments.Models;
using NLog;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace EthPayments
{
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        static async void Main(string[] args)
        {
            try
            {
                var defaultAppsettings = new Dictionary<string, string>
                {
                    { "GethAddress", "http://127.0.0.1:8545/" },
                    { "CallbackUrl", "http://127.0.0.1/CallbackUrl" },
                    { "Type", "token" },
                    { "TokenContractAddress","0x103c3a209da59d3e7c4a89307e66521e081cfdf0" },
                    { "TokenCurrency","GVT" }
                };

                var configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddInMemoryCollection(defaultAppsettings)
                            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                            //.AddJsonFile($"appsettings.Production.json", optional: true)
                            .AddCommandLine(args)
                            .Build();

                var config = configuration.Get<EthPaymentsConfig>();

                config.SetWallets(File.ReadAllLines("wallets.txt"));

                logger.Info("EthPayments started");

                IPayments paymentService;
                switch (config.Type)
                {
                    case "eth":
                        paymentService = new EthPayments(config);
                        break;
                    case "token":
                        paymentService = new TokenPayment(config);
                        break;
                    default:
                        throw new ArgumentNullException();
                }

                if (configuration["FromBlock"] != null)
                {
                    var fromBlock = long.Parse(configuration["FromBlock"]);
                    while (true)
                    {
                        try
                        {
                            logger.Info($"From block: {fromBlock}");
                            fromBlock = paymentService.VerifyWalletsAsync(fromBlock).GetAwaiter().GetResult();
                            Thread.Sleep(1 * 1000);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex.ToString());
                            Thread.Sleep(10 * 1000);
                        }
                    }
                }
                else
                {
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
                            Thread.Sleep(10 * 1000);
                        }
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
