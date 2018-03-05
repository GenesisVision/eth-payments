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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EthPayments
{
	public class TokenPayment : IPayments
	{
		private readonly Logger logger = LogManager.GetCurrentClassLogger();

		private readonly string callbackUrl;
		private readonly EthPaymentsConfig config;
		private readonly Web3Geth web3;

		private readonly HexBigInteger zero = new HexBigInteger(0);
		private readonly HashSet<string> notifiedTxs = new HashSet<string>();
		private readonly HashSet<string> confirmedTxs = new HashSet<string>();
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
			logger.Info($"Contract Address: {config.TokenContractAddress}");

			wallets = new HashSet<string>(config.Wallets);
			web3 = new Web3Geth(config.GethAddress);
			callbackUrl = config.CallbackUrl;

			this.config = config;

			if (!string.IsNullOrEmpty(config.TokenContractAddress))
			{
				var tokenService = new StandardTokenService(web3, config.TokenContractAddress);
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

			foreach (var eventLog in eventLogs)
			{
				if (wallets.Contains(eventLog.Event.AddressTo) && !txs.Contains(eventLog.Log.TransactionHash))
				{
					long blockConfirmations = latestBlockNumber - (long)eventLog.Log.BlockNumber.Value;

					OnNewTransaction(eventLog.Log.TransactionHash, eventLog.Event.Value, eventLog.Event.AddressTo, blockConfirmations);
				}
			}
		}

		public void OnNewTransaction(string transactionHash, BigInteger amount, string to, long blockConfirmations)
		{
			var value = UnitConversion.Convert.FromWei(amount);

			logger.Info($"New transaction: {transactionHash}, amount: {value}");

			notifiedTxs.Add(transactionHash);

			if (string.IsNullOrEmpty(callbackUrl))
				return;

			var client = new HttpClient();

			var isConfirmed = blockConfirmations > blockCount;

			var requestItems = new[]
			{
				new KeyValuePair<string, string>("tx_hash", transactionHash),
				new KeyValuePair<string, string>("address", to),
				new KeyValuePair<string, string>("currency", config.TokenCurrency),
				new KeyValuePair<string, string>("apiKey", config.ApiKey),
				new KeyValuePair<string, string>("gatewayKey", config.ApiKey + "-" + to.ToLower()),
				new KeyValuePair<string, string>("amount", value.ToString(CultureInfo.InvariantCulture)),
				new KeyValuePair<string, string>("amountString", amount.ToString(CultureInfo.InvariantCulture)),
				new KeyValuePair<string, string>("confirmations", blockConfirmations.ToString(CultureInfo.InvariantCulture)),
				new KeyValuePair<string, string>("isConfirmed", isConfirmed.ToString()),
			};

			var data = new FormUrlEncodedContent(requestItems);

			var requestContent = string.Join("&", requestItems.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

			data.Headers.Add("HMAC", HMACSHA512Hex(requestContent));
			data.Headers.Add("Date", DateTime.UtcNow.ToString("R"));

			var res = client.PostAsync(callbackUrl, data).Result;
			var content = res.Content.ReadAsStringAsync().Result;

			if (content.ToLower() == "ok")
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

		private string HMACSHA512Hex(string input)
		{
			var key = Encoding.UTF8.GetBytes(config.ApiSecret);
			using (var hm = new HMACSHA512(key))
			{
				var signed = hm.ComputeHash(Encoding.UTF8.GetBytes(input));
				return BitConverter.ToString(signed).Replace("-", string.Empty);
			}
		}
	}
}
