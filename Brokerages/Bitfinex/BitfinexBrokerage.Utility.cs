﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Orders;
using RestSharp;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace QuantConnect.Brokerages.Bitfinex
{
    /// <summary>
    /// Utility methods for Bitfinex brokerage
    /// </summary>
    public partial class BitfinexBrokerage
    {
        /// <summary>
        /// Unix Epoch
        /// </summary>
        public readonly DateTime dt1970 = new DateTime(1970, 1, 1);
        /// <summary>
        /// Key Header
        /// </summary>
        public const string KeyHeader = "X-BFX-APIKEY";
        /// <summary>
        /// Signature Header
        /// </summary>
        public const string SignatureHeader = "X-BFX-SIGNATURE";
        /// <summary>
        /// Payload Header
        /// </summary>
        public const string PayloadHeader = "X-BFX-PAYLOAD";

        public long GetNonce()
        {
            return (DateTime.UtcNow - dt1970).Ticks;
        }

        /// <summary>
        /// Creates an auth token and adds to the request
        /// </summary>
        /// <param name="request">the rest request</param>
        /// <param name="payload">the body of the request</param>
        /// <returns>a token representing the request params</returns>
        public void SignRequest(IRestRequest request, string payload)
        {
            using (HMACSHA384 hmac = new HMACSHA384(Encoding.UTF8.GetBytes(ApiSecret)))
            {
                byte[] payloadByte = Encoding.UTF8.GetBytes(payload);
                string payloadBase64 = Convert.ToBase64String(payloadByte, Base64FormattingOptions.None);
                string payloadSha384hmac = ByteArrayToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64)));

                request.AddHeader(KeyHeader, ApiKey);
                request.AddHeader(PayloadHeader, payloadBase64);
                request.AddHeader(SignatureHeader, payloadSha384hmac);
            }
        }

        /// <summary>
        /// Creates an auth token for ws auth endppoints
        /// </summary>
        /// <param name="payload">the body of the request</param>
        /// <returns>a token representing the request params</returns>
        private string AuthenticationToken(string payload)
        {
            using (HMACSHA384 hmac = new HMACSHA384(Encoding.UTF8.GetBytes(ApiSecret)))
            {
                return ByteArrayToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        private Func<Messages.Wallet, bool> WalletFilter(AccountType accountType)
        {
            return wallet => wallet.Type.Equals("exchange") && accountType == AccountType.Cash ||
                wallet.Type.Equals("trading") && accountType == AccountType.Margin;
        }

        private decimal GetConversionRate(string currency)
        {
            var response = RateClient.Execute(new RestSharp.RestRequest(Method.GET));
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, (int)response.StatusCode, "GetConversionRate: error returned from conversion rate service."));
                return 0;
            }

            var raw = JsonConvert.DeserializeObject<JObject>(response.Content);
            var rate = raw.SelectToken("rates." + currency.ToUpper()).Value<decimal>();
            if (rate == 0)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, (int)response.StatusCode, "GetConversionRate: zero value returned from conversion rate service."));
                return 0;
            }

            return 1m / rate;
        }

        public Tick GetTick(Symbol symbol)
        {
            string endpoint = GetEndpoint($"pubticker/{symbol.Value}");
            var req = new RestRequest(endpoint, Method.GET);
            var response = ExecuteRestRequest(req, BitfinexEndpointType.Public);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"BitfinexBrokerage.GetTick: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            var tick = JsonConvert.DeserializeObject<Messages.Tick>(response.Content);
            return new Tick(Time.UnixTimeStampToDateTime(tick.Timestamp), symbol, tick.Bid, tick.Ask) { Quantity = tick.Volume };
        }

        public string GetEndpoint(string method)
        {
            return $"/{ApiVersion}/{method}";
        }

        private static OrderStatus ConvertOrderStatus(Messages.Order order)
        {
            if (order.IsLive && order.ExecutedAmount == 0)
            {
                return Orders.OrderStatus.Submitted;
            }
            else if (order.ExecutedAmount > 0 && order.RemainingAmount > 0)
            {
                return Orders.OrderStatus.PartiallyFilled;
            }
            else if (order.RemainingAmount == 0)
            {
                return Orders.OrderStatus.Filled;
            }
            else if (order.IsCancelled)
            {
                return Orders.OrderStatus.Canceled;
            }

            return Orders.OrderStatus.None;
        }

        private static string ConvertOrderType(AccountType accountType, OrderType orderType)
        {
            string outputOrderType = string.Empty;
            switch (orderType)
            {
                case OrderType.Limit:
                case OrderType.Market:
                    outputOrderType = orderType.ToString().ToLower();
                    break;
                case OrderType.StopMarket:
                    outputOrderType = "stop";
                    break;
                default:
                    throw new NotSupportedException($"BitfinexBrokerage.ConvertOrderType: Unsupported order type: {orderType}");
            }

            return (accountType == AccountType.Cash ? "exchange " : "") + outputOrderType;
        }

        private static string ConvertOrderDirection(OrderDirection orderDirection)
        {
            if (orderDirection == OrderDirection.Buy || orderDirection == OrderDirection.Sell)
            {
                return orderDirection.ToString().ToLower();
            }

            throw new NotSupportedException($"BitfinexBrokerage.ConvertOrderDirection: Unsupported order direction: {orderDirection}");
        }

        private static decimal GetOrderPrice(Order order)
        {
            switch (order.Type)
            {
                case OrderType.Limit:
                    return ((LimitOrder)order).LimitPrice;
                case OrderType.Market:
                    return 1;
                case OrderType.StopMarket:
                    return ((StopMarketOrder)order).StopPrice;
            }

            throw new NotSupportedException($"BitfinexBrokerage.ConvertOrderType: Unsupported order type: {order.Type}");
        }

        private Holding ConvertHolding(Messages.Position position)
        {
            return new Holding()
            {
                Symbol = _symbolMapper.GetLeanSymbol(position.Symbol),
                AveragePrice = position.AveragePrice,
                Quantity = position.Amount,
                UnrealizedPnL = position.PL,
                ConversionRate = 1.0m,
                CurrencySymbol = "$",
                Type = SecurityType.Crypto
            };
        }

        private Func<Messages.Order, bool> OrderFilter(AccountType accountType)
        {
            return order => (order.IsExchange && accountType == AccountType.Cash) ||
                (!order.IsExchange && accountType == AccountType.Margin);
        }

        private IRestResponse ExecuteRestRequest(IRestRequest request, BitfinexEndpointType endpointType)
        {
            const int maxAttempts = 10;
            var attempts = 0;
            IRestResponse response;

            do
            {
                _restRateLimiter.WaitToProceed();
                response = RestClient.Execute(request);
                // 429 status code: Too Many Requests
            } while (++attempts < maxAttempts && (int)response.StatusCode == 429);

            return response;
        }

        private static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private bool SubmitOrder(string endpoint, Order order)
        {
            LockStream();

            var payload = new JsonObject();
            payload.Add("request", endpoint);
            payload.Add("nonce", GetNonce().ToString());
            payload.Add("symbol", _symbolMapper.GetBrokerageSymbol(order.Symbol));
            payload.Add("amount", Math.Abs(order.Quantity).ToString(CultureInfo.InvariantCulture));
            payload.Add("side", ConvertOrderDirection(order.Direction));
            payload.Add("type", ConvertOrderType(_algorithm.BrokerageModel.AccountType, order.Type));
            payload.Add("price", GetOrderPrice(order).ToString(CultureInfo.InvariantCulture));

            if (order.BrokerId.Any())
            {
                payload.Add("order_id", long.Parse(order.BrokerId.FirstOrDefault()));
            }

            var orderProperties = order.Properties as BitfinexOrderProperties;
            if (orderProperties != null)
            {
                if (order.Type == OrderType.Limit)
                {
                    payload.Add("is_hidden", orderProperties.Hidden);
                    payload.Add("is_postonly", orderProperties.PostOnly);
                }
            }

            var request = new RestRequest(endpoint, Method.POST);
            request.AddJsonBody(payload.ToString());
            SignRequest(request, payload.ToString());

            var response = ExecuteRestRequest(request, BitfinexEndpointType.Private);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var raw = JsonConvert.DeserializeObject<Messages.Order>(response.Content);

                if (string.IsNullOrEmpty(raw?.Id))
                {
                    var errorMessage = $"Error parsing response from place order: {response.Content}";
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "Bitfinex Order Event") { Status = OrderStatus.Invalid, Message = errorMessage });
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, (int)response.StatusCode, errorMessage));

                    UnlockStream();
                    return true;
                }

                var brokerId = raw.Id;
                if (CachedOrderIDs.ContainsKey(order.Id))
                {
                    CachedOrderIDs[order.Id].BrokerId.Clear();
                    CachedOrderIDs[order.Id].BrokerId.Add(brokerId);
                }
                else
                {
                    order.BrokerId.Add(brokerId);
                    CachedOrderIDs.TryAdd(order.Id, order);
                }

                // Generate submitted event
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "Bitfinex Order Event") { Status = OrderStatus.Submitted });
                Log.Trace($"Order submitted successfully - OrderId: {order.Id}");

                UnlockStream();
                return true;
            }

            var message = $"Order failed, Order Id: {order.Id} timestamp: {order.Time} quantity: {order.Quantity} content: {response.Content}";
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "Bitfinex Order Event") { Status = OrderStatus.Invalid });
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, message));

            UnlockStream();
            return true;
        }
    }
}