using EthPayments.Models;
using Nethereum.Contracts;
using Nethereum.Geth;
using Nethereum.Geth.RPC.Debug.DTOs;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.StandardTokenEIP20;
using Nethereum.StandardTokenEIP20.Events.DTO;
using Nethereum.Util;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

namespace EthPayments
{
    public class TokenPayment : IPayments
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string callbackUrl;
        private readonly Web3Geth web3;

        private readonly HexBigInteger zero = new HexBigInteger(0);
        private readonly HashSet<string> notifiedTxs = new HashSet<string>();
        //private readonly HashSet<string> confirmedTxs = new HashSet<string>();
        private readonly HashSet<string> wallets;
        private readonly Event transfersEvent;

        private const int blockCount = 14;
        private const int blockConfirmedCount = 40;

        private TokenPayment()
        {
        }

        public TokenPayment(EthPaymentsConfig config)
        {
            logger.Info($"Mode: token");
            logger.Info($"Loaded wallets: {config.Wallets.Count()}");
            logger.Info($"Geth address: {config.GethAddress}");
            logger.Info($"Callback url: {config.CallbackUrl}");
            logger.Info($"Contract Address: {config.ContractAddress}");

            wallets = new HashSet<string>(config.Wallets);
            web3 = new Web3Geth(config.GethAddress);
            callbackUrl = config.CallbackUrl;

            if (!string.IsNullOrEmpty(config.ContractAddress))
            {
                var tokenService = new StandardTokenService(web3, config.ContractAddress);
                transfersEvent = tokenService.GetTransferEvent();
            }
        }

        public async Task VerifyWalletsAsync(long? fromBlock = null)
        {
            var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var latestBlockNumber = long.Parse(latestBlock.Value.ToString());

            fromBlock = Math.Min(fromBlock ?? latestBlockNumber - blockConfirmedCount, latestBlockNumber - blockConfirmedCount);
            var toBlock = latestBlockNumber - blockCount;
            
            logger.Debug($"Scanning new transactions {fromBlock}-{toBlock}");
            await VerifyBlockEventsAsync(fromBlock.Value, toBlock, latestBlockNumber, notifiedTxs, false);
        }

        private async Task VerifyBlockEventsAsync(long blockFrom, long blockTo, long latestBlockNumber, HashSet<string> txs, bool isConfirmed)
        {
            if (transfersEvent == null)
            {
                logger.Error("Transfer event does not initialize");
                return;
            }

            var transfersFilter = await transfersEvent.CreateFilterBlockRangeAsync(new BlockParameter((ulong)blockFrom), new BlockParameter((ulong)blockTo));
            var eventLogs = await transfersEvent.GetAllChanges<Transfer>(transfersFilter);

            foreach(var eventLog in eventLogs)
            {
                if (wallets.Contains(eventLog.Event.AddressTo) && !txs.Contains(eventLog.Log.TransactionHash))
                {
                    OnNewTransaction(eventLog.Log.TransactionHash, eventLog.Event.Value, eventLog.Event.AddressTo);
                }
            }
        }

        public void OnNewTransaction(string transactionHash, BigInteger amount, string to)
        {
            var value = UnitConversion.Convert.FromWei(amount);

            logger.Info($"New transaction: {transactionHash}, amount: {value}");

            notifiedTxs.Add(transactionHash);

            if (string.IsNullOrEmpty(callbackUrl))
                return;


            // todo: notify

        }
    }
}
