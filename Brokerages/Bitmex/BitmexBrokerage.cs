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

        public override bool CancelOrder(Order order)
        {
            throw new NotImplementedException();
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

        public override List<Order> GetOpenOrders()
        {
            throw new NotImplementedException();
        }

        public override void OnMessage(object sender, WebSocketMessage e)
        {
            throw new NotImplementedException();
        }

        public override bool PlaceOrder(Order order)
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

        public override bool UpdateOrder(Order order)
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
