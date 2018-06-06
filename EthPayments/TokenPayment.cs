using EthPayments.Models;
using Nethereum.Contracts;
using Nethereum.Geth;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.StandardTokenEIP20;
using Nethereum.StandardTokenEIP20.Events.DTO;
using Nethereum.Util;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace EthPayments
{
    public class TokenPayment : IPayments
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string callbackUrl;
        private readonly EthPaymentsConfig config;
        private readonly NotificationSender notificationSender;
        private readonly Web3Geth web3;

        private readonly HexBigInteger zero = new HexBigInteger(0);
        private readonly HashSet<string> notifiedTxs = new HashSet<string>();
        private readonly HashSet<string> confirmedTxs = new HashSet<string>();
        //private readonly HashSet<string> confirmedTxs = new HashSet<string>();
        private readonly HashSet<string> wallets;
        private readonly Event transfersEvent;

        private const int blockCount = 14;
        private const int blockConfirmedCount = 40;

        public TokenPayment(EthPaymentsConfig config)
        {
            logger.Info($"Mode: token");
            logger.Info($"Loaded wallets: {config.Wallets.Count()}");
            logger.Info($"Geth address: {config.GethAddress}");
            logger.Info($"Callback url: {config.CallbackUrl}");
            logger.Info($"{nameof(config.TokenContractAddress)}: {config.TokenContractAddress}");
            logger.Info($"{nameof(config.TokenCurrency)}: {config.TokenCurrency}");

            wallets = new HashSet<string>(config.Wallets);
            web3 = new Web3Geth(config.GethAddress);
            callbackUrl = config.CallbackUrl;

            this.config = config;

            notificationSender = new NotificationSender(callbackUrl, config.ApiKey, config.ApiSecret);


            if (!string.IsNullOrEmpty(config.TokenContractAddress))
            {
                var tokenService = new StandardTokenService(web3, config.TokenContractAddress);
                transfersEvent = tokenService.GetTransferEvent();
            }
        }

        public async Task<long> VerifyWalletsAsync(long? fromBlock = null)
        {
            var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var latestBlockNumber = long.Parse(latestBlock.Value.ToString());

            fromBlock = Math.Min(fromBlock ?? latestBlockNumber - blockConfirmedCount, latestBlockNumber - blockConfirmedCount);
            //var toBlock = latestBlockNumber - blockCount;

            logger.Debug($"Scanning new transactions {fromBlock}-{latestBlockNumber}");
            await VerifyBlockEventsAsync(fromBlock.Value, latestBlockNumber, latestBlockNumber);

            return latestBlockNumber;
        }

        private async Task VerifyBlockEventsAsync(long blockFrom, long blockTo, long latestBlockNumber)
        {
            if (transfersEvent == null)
            {
                logger.Error("Transfer event does not initialize");
                return;
            }

            var transfersFilter = await transfersEvent.CreateFilterBlockRangeAsync(new BlockParameter((ulong)blockFrom), new BlockParameter((ulong)blockTo));
            var eventLogs = await transfersEvent.GetAllChanges<Transfer>(transfersFilter);

            foreach (var eventLog in eventLogs)
            {
                if (wallets.Contains(eventLog.Event.AddressTo))
                {
                    long blockConfirmations = latestBlockNumber - (long)eventLog.Log.BlockNumber.Value;

                    OnNewTransaction(eventLog.Log.TransactionHash, eventLog.Event.Value, eventLog.Event.AddressTo, blockConfirmations);
                }
            }
        }

        public void OnNewTransaction(string transactionHash, BigInteger amountWei, string to, long blockConfirmations)
        {
            var amount = UnitConversion.Convert.FromWei(amountWei);
            var isConfirmed = blockConfirmations > blockCount;

            if(!isConfirmed && !notifiedTxs.Contains(transactionHash))
            {
                logger.Info($"New transaction: {transactionHash}, block: {blockConfirmations}, amount: {amount}, isConfirmed: {isConfirmed}");

                var result = notificationSender.Send(transactionHash, amount, amountWei, config.TokenCurrency, to, isConfirmed, blockConfirmations).GetAwaiter().GetResult();
                if (result)
                {
                    notifiedTxs.Add(transactionHash);
                }
            }

            if (isConfirmed && !confirmedTxs.Contains(transactionHash))
            {
                logger.Info($"New Confirmed transaction: {transactionHash}, block: {blockConfirmations}, amount: {amount}, isConfirmed: {isConfirmed}");

                var result = notificationSender.Send(transactionHash, amount, amountWei, config.TokenCurrency, to, isConfirmed, blockConfirmations).GetAwaiter().GetResult();
                if (result)
                {
                    confirmedTxs.Add(transactionHash);
                }
            }
        }
    }
}
