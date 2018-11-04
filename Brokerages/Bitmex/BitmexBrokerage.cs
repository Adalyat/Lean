using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using RestSharp;
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.Bitmex
{
    public class BitmexBrokerage : BaseWebsocketsBrokerage, IDataQueueHandler
    {
        private readonly IAlgorithm _algorithm;
        private readonly ISecurityProvider _securityProvider;

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

        public override List<Holding> GetAccountHoldings()
        {
            throw new NotImplementedException();
        }

        public override List<Cash> GetCashBalance()
        {
            throw new NotImplementedException();
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
    }
}
