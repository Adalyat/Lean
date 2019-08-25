using Newtonsoft.Json;
using QuantConnect.Logging;
using QuantConnect.Orders;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    public partial class BitmexBrokerage
    {
        /// <summary>
        /// Returns relative endpoint for current Bitmex domain
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private string GetEndpoint(string method)
        {
            return $"/api/v1{method}";
        }

        /// <summary>
        /// Creates an auth token and adds to the request
        /// https://bitmex.com/app/apiKeysUsage
        /// </summary>
        /// <param name="request">the rest request</param>
        /// <param name="endpoint">the request endpoint including query string</param>
        /// <param name="payload">the body of the request</param>
        /// <returns>a token representing the request params</returns>
        private void SignRequest(IRestRequest request, string endpoint, string payload = null)
        {
            string expires = GetExpires().ToString(CultureInfo.InvariantCulture);
            string message = request.Method + endpoint + expires + payload;
            byte[] signatureBytes = hmacsha256(Encoding.UTF8.GetBytes(ApiSecret), Encoding.UTF8.GetBytes(message));
            string signatureString = ByteArrayToString(signatureBytes);

            request.AddHeader("api-key", ApiKey);
            request.AddHeader("api-expires", expires);
            request.AddHeader("api-signature", signatureString);
        }

        /// <summary>
        /// A UNIX timestamp in seconds after which the request is no longer valid.
        /// This is to prevent replay attacks.
        /// </summary>
        /// <returns></returns>
        private long GetExpires()
        {
            return (long)Time.DateTimeToUnixTimeStamp(DateTime.UtcNow.AddMinutes(5)); // set expires 5 minutes in the future
        }

        private byte[] hmacsha256(byte[] keyByte, byte[] messageBytes)
        {
            using (var hash = new HMACSHA256(keyByte))
            {
                return hash.ComputeHash(messageBytes);
            }
        }

        private static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        /// <summary>
        /// If an IP address exceeds a certain number of requests per minute
        /// the 429 status code and and an additional header, Retry-After, will be returned
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private IRestResponse ExecuteRestRequest(IRestRequest request)
        {
            const int maxAttempts = 10;
            var attempts = 0;
            IRestResponse response;

            do
            {
                if (!_restRateLimiter.WaitToProceed(TimeSpan.Zero))
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "RateLimit",
                        "The API request has been rate limited. To avoid this message, please reduce the frequency of API calls."));

                    _restRateLimiter.WaitToProceed();
                }

                response = RestClient.Execute(request);
                // 429 status code: Too Many Requests
            } while (++attempts < maxAttempts && (int)response.StatusCode == 429);

            return response;
        }

        /// <summary>
        /// Provides the current tickers price
        /// </summary>
        /// <returns></returns>
        public Messages.PriceTicker GetTicker(Symbol symbol)
        {
            string endpoint = GetEndpoint($"/instrument?symbol={symbol.Value}&count=100&reverse=false");
            var req = new RestRequest(endpoint, Method.GET);
            var response = ExecuteRestRequest(req);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"BitmexBrokerage.GetTicker: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            return JsonConvert.DeserializeObject<Messages.PriceTicker[]>(response.Content).FirstOrDefault();
        }

        private decimal GetConversionRate(Symbol symbol)
        {
            try
            {
                return _priceProvider.GetLastPrice(symbol);
            }
            catch (Exception e)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, 0, $"GetConversionRate: {e.Message}"));
                return 0;
            }
        }

        /// <summary>
        /// Converts an Bitmex position into a LEAN holding.
        /// </summary>
        private Holding ConvertHolding(Messages.Position position)
        {
            var holding = new Holding
            {
                Symbol = _symbolMapper.GetLeanSymbol(position.Symbol),
                Type = SecurityType.Crypto,
                Quantity = position.Quantity,
                AveragePrice = position.AveragePrice,
                ConversionRate = 1.0m,
                CurrencySymbol = "$",
                UnrealizedPnL = position.UnrealisedPnl
            };
            
            try
            {
                var tick = GetTicker(holding.Symbol);
                holding.MarketPrice = tick.Price;
            }
            catch (Exception)
            {
                Log.Error($"BitmexBrokerage.ConvertHolding(): failed to set {holding.Symbol} market price");
                throw;
            }

            return holding;
        }

        private static readonly Dictionary<string, OrderStatus> _statusMapper = new Dictionary<string, OrderStatus>(StringComparer.OrdinalIgnoreCase)
        {
            { "New", OrderStatus.Submitted},
            { "PartiallyFilled", OrderStatus.PartiallyFilled},
            { "Filled", OrderStatus.Filled},
            { "Canceled", OrderStatus.Canceled}
        };

        private static OrderStatus ConvertOrderStatus(Messages.Order item)
        {
            return ConvertOrderStatus(item.Status);
        }

        private static OrderStatus ConvertOrderStatus(string status)
        {
            if (string.IsNullOrEmpty(status))
                return OrderStatus.None;

            if (_statusMapper.ContainsKey(status))
            {
                return _statusMapper[status];
            }

            return OrderStatus.None;
        }

        private static readonly Dictionary<Resolution, string> _knownResolutions = new Dictionary<Resolution, string>()
        {
            { Resolution.Minute, "1m" },
            { Resolution.Hour,   "1h" },
            { Resolution.Daily,  "1d" }
        };

        private static string ConvertResolution(Resolution resolution)
        {
            if (_knownResolutions.ContainsKey(resolution))
            {
                return _knownResolutions[resolution];
            }
            else
            {
                throw new ArgumentException($"BitmexBrokerage.ConvertResolution: Unsupported resolution type: {resolution}");
            }
        }
    }
}
