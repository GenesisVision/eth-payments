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
    public class NotificationSender
    {
        private string callbackUrl;
        private string apiKey;
        private string apiSecret;
        private HttpClient client;
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        public NotificationSender(string callbackUrl, string apiKey, string apiSecret)
        {
            this.callbackUrl = callbackUrl;
            this.apiKey = apiKey;
            this.apiSecret = apiSecret;
            this.client = new HttpClient();
        }

        public async Task<bool> Send(string transactionHash, decimal amount, BigInteger amountWei, string currency, string to, bool isConfirmed, long blockConfirmations)
        {
            if (string.IsNullOrEmpty(callbackUrl))
            {
                throw new ArgumentNullException(nameof(callbackUrl));
            }

            var requestItems = new[]
            {
                new KeyValuePair<string, string>("transactionHash", transactionHash),
                new KeyValuePair<string, string>("toAddress", to),
                new KeyValuePair<string, string>("currency", currency),
                new KeyValuePair<string, string>("apiKey", apiKey),
                new KeyValuePair<string, string>("gatewayKey", apiKey + "-" + to.ToLower()),
                new KeyValuePair<string, string>("amount", amount.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("amountBigInt", amountWei.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("blockConfirmations", blockConfirmations.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("isConfirmed", isConfirmed.ToString()),
                new KeyValuePair<string, string>("nonce", DateTime.UtcNow.ToString("R"))
            };

            var data = new FormUrlEncodedContent(requestItems);
            var requestContent = string.Join("&", requestItems.OrderBy(x => x.Key).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            data.Headers.Add("HMAC", HMACSHA512Hex(requestContent));

            try
            {
                var res = await client.PostAsync(callbackUrl, data);
                var content = await res.Content.ReadAsStringAsync();

                if (content.ToLower() == "ok")
                {
                    logger.Info($"NotificationSender OK {transactionHash};{to}; {amount} : {content} ");
                    return true;
                }
                else
                {
                    logger.Error($"NotificationSender ER {transactionHash};{to}; {amount} : {content} ");
                    return false;
                }
            }
            catch (Exception e)
            {
                logger.Error($"NotificationSender Exception {transactionHash};{to}; {amount} - {e.ToString()}");
                return false;
            }
        }

        private string HMACSHA512Hex(string input)
        {
            var key = Encoding.UTF8.GetBytes(apiSecret);
            using (var hm = new HMACSHA512(key))
            {
                var signed = hm.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(signed).Replace("-", string.Empty);
            }
        }
    }
}
