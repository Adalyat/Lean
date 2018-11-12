using Newtonsoft.Json;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using QuantConnect.Logging;
using System.Globalization;
using System.Text;
using QuantConnect.Data.Market;
using QuantConnect.Orders.Fees;

namespace QuantConnect.Brokerages.Bitmex
{
    public partial class BitmexBrokerage : BaseWebsocketsBrokerage, IDataQueueHandler
    {
        private readonly IAlgorithm _algorithm;
        private readonly ISecurityProvider _securityProvider;
        private readonly RateGate _restRateLimiter = new RateGate(150, TimeSpan.FromMinutes(5));
        private readonly IPriceProvider _priceProvider;
        private readonly BitmexSymbolMapper _symbolMapper = new BitmexSymbolMapper();

        /// <summary>
        /// Constructor for brokerage
        /// </summary>
        /// <param name="wssUrl">websockets url</param>
        /// <param name="restUrl">rest api url</param>
        /// <param name="apiKey">api key</param>
        /// <param name="apiSecret">api secret</param>
        /// <param name="algorithm">the algorithm instance is required to retreive account type</param>
        public BitmexBrokerage(string wssUrl, string restUrl, string apiKey, string apiSecret, IAlgorithm algorithm, IPriceProvider priceProvider)
            : base(wssUrl, new WebSocketWrapper(), new RestClient(restUrl), apiKey, apiSecret, Market.Bitmex, "BitMEX")
        {
            _algorithm = algorithm;
            _securityProvider = algorithm?.Portfolio;
            _priceProvider = priceProvider;
        }

        public override bool IsConnected
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Gets all open positions
        /// </summary>
        /// <returns></returns>
        public override List<Holding> GetAccountHoldings()
        {
            var endpoint = GetEndpoint($"/position?filter={WebUtility.UrlEncode("{\"isOpen\":true}")}");
            var request = new RestRequest(endpoint, Method.GET);

            SignRequest(request, null);

            var response = ExecuteRestRequest(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"BitmexBrokerage.GetAccountHoldings: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            var positions = JsonConvert.DeserializeObject<Messages.Position[]>(response.Content);
            return positions
                .Where(p => p.Quantity != 0)
                .Select(ConvertHolding)
                .ToList();
        }

        /// <summary>
        /// Gets the total account cash balance for specified account type
        /// </summary>
        /// <returns></returns>
        public override List<CashAmount> GetCashBalance()
        {
            var list = new List<CashAmount>();
            var endpoint = GetEndpoint("/user/margin?currency=all");
            var request = new RestRequest(endpoint, Method.GET);

            SignRequest(request, null);

            var response = ExecuteRestRequest(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"BitmexBrokerage.GetCashBalance: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            var availableWallets = JsonConvert.DeserializeObject<Messages.Wallet[]>(response.Content)
                .Where(w => w.Amount > 0);
            foreach (var item in availableWallets)
            {
                list.Add(new CashAmount(item.Amount, item.Currency.ToUpper()));
            }

            return list;
        }

        public IEnumerable<BaseData> GetNextTicks()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets all orders not yet closed
        /// </summary>
        /// <returns></returns>
        public override List<Order> GetOpenOrders()
        {
            var list = new List<Order>();
            var endpoint = GetEndpoint($"/order?filter={WebUtility.UrlEncode("{\"open\":true}")}");
            var request = new RestRequest(endpoint, Method.GET);

            SignRequest(request, null);
            var response = ExecuteRestRequest(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"BitmexBrokerage.GetOpenOrders: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            var orders = JsonConvert.DeserializeObject<Messages.Order[]>(response.Content);
            foreach (var item in orders)
            {
                Order order;
                switch (item.Type.ToUpper())
                {
                    case "MARKET":
                        order = new MarketOrder { Price = item.Price.Value };
                        break;
                    case "LIMIT":
                        order = new LimitOrder { LimitPrice = item.Price.Value };
                        break;
                    case "STOP":
                        order = new StopMarketOrder { StopPrice = item.StopPrice.Value };
                        break;
                    case "STOPLIMIT":
                        order = new StopLimitOrder { StopPrice = item.StopPrice.Value, LimitPrice = item.Price.Value };
                        break;
                    default:
                        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, (int)response.StatusCode,
                            "BitmexBrokerage.GetOpenOrders: Unsupported order type returned from brokerage: " + item.Type));
                        continue;
                }

                order.Quantity = item.Side == "sell" ? -item.Quantity : item.Quantity;
                order.BrokerId = new List<string> { item.Id.ToString() };
                order.Symbol = _symbolMapper.GetLeanSymbol(item.Symbol);
                order.Time = item.Timestamp;
                order.Status = ConvertOrderStatus(item);
                list.Add(order);
            }

            foreach (var item in list)
            {
                if (item.Status.IsOpen())
                {
                    var cached = CachedOrderIDs.Where(c => c.Value.BrokerId.Contains(item.BrokerId.First()));
                    if (cached.Any())
                    {
                        CachedOrderIDs[cached.First().Key] = item;
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            IDictionary<string, object> body = new Dictionary<string, object>()
            {
                { "symbol", _symbolMapper.GetBrokerageSymbol(order.Symbol) },
                { "orderQty", Math.Abs(order.Quantity).ToString(CultureInfo.InvariantCulture) },
                { "side", order.Direction.ToString() }
            };

            switch (order.Type)
            {
                case OrderType.Limit:
                    body["price"] = (order as LimitOrder).LimitPrice.ToString(CultureInfo.InvariantCulture);
                    body["type"] = "Limit";
                    break;
                case OrderType.Market:
                    body["type"] = "Market";
                    break;
                case OrderType.StopLimit:
                    body["type"] = "StopLimit";
                    body["price"] = (order as StopLimitOrder).LimitPrice.ToString(CultureInfo.InvariantCulture);
                    body["stopPx"] = (order as StopLimitOrder).StopPrice.ToString(CultureInfo.InvariantCulture);
                    break;
                case OrderType.StopMarket:
                    body["type"] = "Stop";
                    body["stopPx"] = (order as StopMarketOrder).StopPrice.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new NotSupportedException($"BitmexBrokerage.ConvertOrderType: Unsupported order type: {order.Type}");
            }

            if (order.Type == OrderType.Limit || order.Type == OrderType.StopLimit)
            {
                var orderProperties = order.Properties as BitmexOrderProperties;
                if (orderProperties != null)
                {
                    body["displayQty"] = orderProperties.Hidden ? (decimal?)0 : null;
                    body["execInst"] = orderProperties.PostOnly ? "ParticipateDoNotInitiate" : "";
                }
            }

            var endpoint = GetEndpoint("/order");
            var request = new RestRequest(endpoint, Method.POST);
            request.AddParameter(
                "application/x-www-form-urlencoded",
                Encoding.UTF8.GetBytes(body.ToQueryString()),
                ParameterType.RequestBody
            );
            SignRequest(request, body.ToQueryString());

            var response = ExecuteRestRequest(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var raw = JsonConvert.DeserializeObject<Messages.Order>(response.Content);

                if (raw?.Id == null)
                {
                    var errorMessage = $"Error parsing response from place order: {response.Content}";
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Bitmex Order Event") { Status = OrderStatus.Invalid, Message = errorMessage });
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, (int)response.StatusCode, errorMessage));

                    return true;
                }

                var brokerId = raw.Id.ToString();
                if (CachedOrderIDs.ContainsKey(order.Id))
                {
                    order.BrokerId.Clear();
                    order.BrokerId.Add(brokerId);
                }
                else
                {
                    order.BrokerId.Add(brokerId);
                    CachedOrderIDs.TryAdd(order.Id, order);
                }

                // Generate submitted event
                var evnt = new OrderEvent(
                    order,
                    raw.Timestamp,
                    OrderFee.Zero,
                    "Bitmex Order Event")
                {
                    Status = OrderStatus.Submitted
                };
                OnOrderEvent(evnt);
                Log.Trace($"Order submitted successfully - OrderId: {order.Id}");

                return true;
            }

            var message = $"Order failed, Order Id: {order.Id} timestamp: {order.Time} quantity: {order.Quantity} content: {response.Content}";
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Bitmex Order Event") { Status = OrderStatus.Invalid });
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, message));

            return true;
        }

        public override bool UpdateOrder(Order order)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was submitted for cancellation, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            var success = new List<bool>();
            IDictionary<string, object> body = new Dictionary<string, object>();
            foreach (var id in order.BrokerId)
            {
                body["orderID"] = id;
                var request = new RestRequest(GetEndpoint("/order"), Method.DELETE);
                SignRequest(request, body.ToQueryString());
                request.AddParameter(
                    "application/x-www-form-urlencoded",
                    Encoding.UTF8.GetBytes(body.ToQueryString()),
                    ParameterType.RequestBody
                );

                var response = ExecuteRestRequest(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var parsed = JsonConvert.DeserializeObject<Messages.Order[]>(response.Content);
                    var cancelledOrder = parsed.FirstOrDefault(o => string.Equals(o.Id.ToString(), id));
                    success.Add(parsed != null && string.IsNullOrEmpty(cancelledOrder.Error));
                }
                else
                {
                    success.Add(false);
                }
            }

            var cancellationSubmitted = false;
            if (success.All(a => a))
            {
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Bitmex Order Event") { Status = OrderStatus.Canceled });
                cancellationSubmitted = true;
            }

            return cancellationSubmitted;
        }

        public override void OnMessage(object sender, WebSocketMessage e)
        {
            throw new NotImplementedException();
        }

        public override void Subscribe(IEnumerable<Symbol> symbols)
        {
            throw new NotImplementedException();
        }

        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            _restRateLimiter.Dispose();
        }
    }
}
