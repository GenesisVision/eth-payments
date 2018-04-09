using EthPayments.Models;
using Nethereum.Geth;
using Nethereum.Geth.RPC.Debug.DTOs;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EthPayments
{
    public class EthPayments : IPayments
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string callbackUrl;
        private readonly NotificationSender notificationSender;
        private readonly Web3Geth web3;

        private readonly HexBigInteger zero = new HexBigInteger(0);
        private readonly HashSet<string> notifiedTxs = new HashSet<string>();
        private readonly HashSet<string> confirmedTxs = new HashSet<string>();
        private readonly HashSet<string> wallets;
        private readonly HashSet<string> walletsTrimmed;

        private const int blockCount = 14;
        private const int blockConfirmedCount = 40;
        private const string currency = "ETH";

        public EthPayments(EthPaymentsConfig config)
        {
            logger.Info($"Mode: ETH");
            logger.Info($"Loaded wallets: {config.Wallets.Count()}");
            logger.Info($"Geth address: {config.GethAddress}");
            logger.Info($"Callback url: {config.CallbackUrl}");

            wallets = new HashSet<string>(config.Wallets);
            walletsTrimmed = new HashSet<string>(config.WalletsTrimmed);
            web3 = new Web3Geth(config.GethAddress);
            callbackUrl = config.CallbackUrl;
            notificationSender = new NotificationSender(callbackUrl, config.ApiKey, config.ApiSecret);
        }

        public async Task<long> VerifyWalletsAsync(long? fromBlock = null)
        {
            var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var latestBlockNumber = long.Parse(latestBlock.Value.ToString());
            var latestBlockNumberFrom = latestBlockNumber - blockCount;

            logger.Debug($"Scanning new transactions {latestBlockNumberFrom}-{latestBlockNumber}");
            await VerifyBlockAsync(latestBlockNumberFrom, latestBlockNumber, latestBlockNumber, notifiedTxs, false);

            fromBlock = Math.Min(fromBlock ?? latestBlockNumber - blockConfirmedCount, latestBlockNumber - blockConfirmedCount);
            var fromBlockEnd = latestBlockNumber - blockCount;

            logger.Debug($"Scanning confirmed transactions {fromBlock}-{fromBlockEnd}");
            await VerifyBlockAsync(fromBlock.Value, fromBlockEnd, latestBlockNumber, confirmedTxs, true);

            return fromBlockEnd;
        }

        private async Task VerifyBlockAsync(long blockFrom, long blockTo, long latestBlockNumber, HashSet<string> txs, bool isConfirmed)
        {
            for (var i = blockFrom; i <= blockTo; i++)
            {
                var block = await web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(i));
                logger.Debug($" scanning block {i} ({block.Transactions.Count()} transactions)");

                foreach (var transaction in block.Transactions.Where(x => !txs.Contains(x.TransactionHash)))
                {
                    if (wallets.Contains(transaction.To))
                    {
                        OnNewTransaction(transaction.TransactionHash, transaction.Value, transaction.To, latestBlockNumber - i, isConfirmed, false);
                    }
                    else if (transaction.Value.Value == zero.Value && !string.IsNullOrEmpty(transaction.Input))
                    {
                        var walletExists = false;
                        Parallel.ForEach(walletsTrimmed, w =>
                        {
                            if (transaction.Input.Contains(w))
                            {
                                walletExists = true;
                                return;
                            }
                        });

                        if (walletExists)
                        {
                            await VerifyTransactionByLogsAsync(transaction, latestBlockNumber - i, isConfirmed);
                        }
                    }
                }
            }
        }

        private async Task VerifyTransactionByLogsAsync(Transaction transaction, long blockConfirmations, bool isConfirmed)
        {
            logger.Trace($"  trace transaction {transaction.TransactionHash}");

            var transactionTrace = await web3.Debug.TraceTransaction.SendRequestAsync(transaction.TransactionHash,
                new TraceTransactionOptions { DisableMemory = true, DisableStack = false, FullStorage = false, DisableStorage = true });

            var failed = transactionTrace["failed"];
            if (failed == null || Convert.ToBoolean(failed.ToString()))
                return;

            var logs = transactionTrace["structLogs"];
            if (logs != null)
            {
                var structLogs = JsonConvert.DeserializeObject<List<StructLogs>>(logs.ToString());
                if (structLogs != null && structLogs.Any())
                {
                    // Type_TraceAddress: call_0 & call_0_0
                    var callOps = structLogs.Where(x => x.Op == "CALL").ToList();
                    if (callOps.Any() && callOps.First().Stack.Count > 2)
                    {
                        var stack = callOps.Last().Stack;
#if DEBUG
                        var stackLog = string.Join(Environment.NewLine, stack);
#endif
                        var amountStr = stack[stack.Count - 3];
                        var hexAmount = new HexBigInteger(amountStr);
                        var amount = UnitConversion.Convert.FromWei(hexAmount);
                        var txToStr = stack[stack.Count - 2];
                        var txTo = txToStr.Substring(txToStr.Length - 40);

                        if (walletsTrimmed.Contains(txTo))
                        {
                            var hexBalance = await web3.Eth.GetBalance.SendRequestAsync(txTo, new BlockParameter(transaction.BlockNumber));
                            var balance = UnitConversion.Convert.FromWei(hexBalance);
                            if (balance >= amount)
                            {
                                logger.Warn($"Find by trace! Tx: {transaction.TransactionHash}, to: {txTo}, amount: {amount}, balance: {balance}");
                                OnNewTransaction(transaction.TransactionHash, hexAmount, "0x" + txTo, blockConfirmations, isConfirmed, true);
                            }
                            else
                            {
                                logger.Error($"Find by trace! Tx: {transaction.TransactionHash}, to: {txTo}, amount: {amount}, balance: {balance}");
                            }
                        }
                    }
                }
            }
        }

        public void OnNewTransaction(string transactionHash, HexBigInteger amountWei, string to, long blockConfirmations, bool isConfirmed, bool txFromTrace)
        {
            var amount = UnitConversion.Convert.FromWei(amountWei);
            logger.Info($"New transaction: {transactionHash}, block: {blockConfirmations}, amount: {amount}, isConfirmed: {isConfirmed}, txFromTrace: {txFromTrace}");

            var result = notificationSender.Send(transactionHash, amount, amountWei, currency, to, isConfirmed, blockConfirmations).GetAwaiter().GetResult();
            if (result)
            {
                if (isConfirmed)
                {
                    confirmedTxs.Add(transactionHash);
                }
                else
                {
                    notifiedTxs.Add(transactionHash);
                }
            }
        }
    }
}
