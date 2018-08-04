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
using QuantConnect.Orders;
using QuantConnect.Securities;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using System.Collections.Concurrent;
using QuantConnect.Util;
using Newtonsoft.Json.Linq;
using com.sun.corba.se.impl.protocol.giopmsgheaders;
using System.Globalization;
using QuantConnect.Data.Market;

namespace QuantConnect.Brokerages.Bitfinex
{
    public partial class BitfinexBrokerage
    {
        private const string ApiVersion = "v1";
        private readonly IAlgorithm _algorithm;
        private readonly ISecurityProvider _securityProvider;
        private readonly ConcurrentQueue<WebSocketMessage> _messageBuffer = new ConcurrentQueue<WebSocketMessage>();
        private readonly object channelLocker = new object();
        private volatile bool _streamLocked;
        internal enum BitfinexEndpointType { Public, Private }
        private readonly RateGate _restRateLimiter = new RateGate(8, TimeSpan.FromMinutes(1));
        private readonly ConcurrentDictionary<Symbol, OrderBook> _orderBooks = new ConcurrentDictionary<Symbol, OrderBook>();
        private readonly object closeLocker = new object();
        private readonly List<string> _pendingClose = new List<string>();
        /// <summary>
        /// Rest client used to call missing conversion rates
        /// </summary>
        public IRestClient RateClient { get; set; }

        /// <summary>
        /// Locking object for the Ticks list in the data queue handler
        /// </summary>
        protected readonly object TickLocker = new object();

        /// <summary>
        /// Constructor for brokerage
        /// </summary>
        /// <param name="wssUrl">websockets url</param>
        /// <param name="websocket">instance of websockets client</param>
        /// <param name="restClient">instance of rest client</param>
        /// <param name="apiKey">api key</param>
        /// <param name="apiSecret">api secret</param>
        /// <param name="algorithm">the algorithm instance is required to retreive account type</param>
        public BitfinexBrokerage(string wssUrl, IWebSocket websocket, IRestClient restClient, string apiKey, string apiSecret, IAlgorithm algorithm)
            : base(wssUrl, websocket, restClient, apiKey, apiSecret, Market.Bitfinex, "Bitfinex")
        {
            _algorithm = algorithm;
            _securityProvider = algorithm.Portfolio;
            RateClient = new RestClient("http://data.fixer.io/api/latest?base=usd&access_key=26a2eb9f13db3f14b6df6ec2379f9261");

            WebSocket.Open += (sender, args) =>
            {
                SubscribeAuth();
            };
        }

        /// <summary>
        /// Wss message handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public override void OnMessage(object sender, WebSocketMessage e)
        {
            // Verify if we're allowed to handle the streaming packet yet; while we're placing an order we delay the
            // stream processing a touch.
            try
            {
                if (_streamLocked)
                {
                    _messageBuffer.Enqueue(e);
                    return;
                }
            }
            catch (Exception err)
            {
                Log.Error(err);
            }

            OnMessageImpl(sender, e);
        }

        /// <summary>
        /// Subscribes to the authenticated channels (using an single streaming channel)
        /// </summary>
        public void SubscribeAuth()
        {
            var authNonce = GetNonce();
            var authPayload = "AUTH" + authNonce;
            var authSig = AuthenticationToken(authPayload);
            WebSocket.Send(JsonConvert.SerializeObject(new
            {
                @event = "auth",
                apiKey = ApiKey,
                authNonce,
                authPayload,
                authSig
            }));

            Log.Trace("BitfinexBrokerage.Subscribe: Sent subscribe.");
        }

        /// <summary>
        /// Subscribes to the requested symbols (using an individual streaming channel)
        /// </summary>
        /// <param name="symbols">The list of symbols to subscribe</param>
        public override void Subscribe(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (symbol.Value.Contains("UNIVERSE") ||
                    !_symbolMapper.IsKnownBrokerageSymbol(symbol.Value) ||
                    symbol.SecurityType != _symbolMapper.GetLeanSecurityType(symbol.Value))
                {
                    continue;
                }

                WebSocket.Send(JsonConvert.SerializeObject(new
                {
                    @event = "subscribe",
                    channel = "book",
                    pair = _symbolMapper.GetBrokerageSymbol(symbol)
                }));
            }

            Log.Trace("BitfinexBrokerage.Subscribe: Sent subscribe.");
        }

        /// <summary>
        /// Ends current subscriptions
        /// </summary>
        public void Unsubscribe(IEnumerable<Symbol> symbols)
        {
            if (WebSocket.IsOpen)
            {
                var map = ChannelList.ToDictionary(k => k.Value.Symbol, k => k.Key, StringComparer.InvariantCultureIgnoreCase);
                foreach (var symbol in symbols)
                {
                    if (map.ContainsKey(symbol.Value))
                    {
                        WebSocket.Send(JsonConvert.SerializeObject(new
                        {
                            @event = "unsubscribe",
                            channelId = map[symbol.Value]
                        }));
                    }
                }
            }
        }

        /// <summary>
        /// Implementation of the OnMessage event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMessageImpl(object sender, WebSocketMessage e)
        {
            try
            {
                var token = JToken.Parse(e.Message);

                if (token is JArray)
                {
                    int channel = token[0].ToObject<int>();
                    if (channel != 0 && token[1].Type != JTokenType.String)
                    {
                        if (token.Count() == 2)
                        {
                            OnSnapshot(
                                token[0].ToObject<string>(),
                                token[1].ToObject<string[][]>()
                            );
                        }
                        else
                        {
                            OnUpdate(
                                token[0].ToObject<string>(),
                                token.ToObject<string[]>().Skip(1).ToArray()
                            );
                        }
                    }
                    else if (channel == 0)
                    {
                        string term = token[1].ToObject<string>();
                        switch (term.ToLower())
                        {
                            case "oc":
                                OnOrderClose(token[2].ToObject<string[]>());
                                return;
                            case "tu":
                                EmitFillOrder(token[2].ToObject<string[]>());
                                return;
                            default:
                                return;
                        }
                    }
                }
                else if (token is JObject)
                {
                    Messages.BaseMessage raw = token.ToObject<Messages.BaseMessage>();
                    switch (raw.Event.ToLower())
                    {
                        case "subscribed":
                            OnSubscribe(token.ToObject<Messages.OrderBookSubscription>());
                            return;
                        case "unsubscribed":
                            OnUnsubscribe(token.ToObject<Messages.OrderBookUnsubscribing>());
                            return;
                        case "info":
                        case "ping":
                            return;
                        case "error":
                            Log.Trace($"BitfinexWebsocketsBrokerage.OnMessage: Error: {e.Message}");
                            return;
                        default:
                            Log.Trace($"BitfinexWebsocketsBrokerage.OnMessage: Unexpected message format: {e.Message}");
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Parsing wss message failed. Data: {e.Message} Exception: {exception}"));
                throw;
            }
        }

        private void OnSubscribe(Messages.OrderBookSubscription data)
        {
            try
            {
                Channel existing = null;
                lock (channelLocker)
                {
                    if (!ChannelList.TryGetValue(data.ChannelId, out existing))
                    {
                        ChannelList[data.ChannelId] = new Channel() { Name = data.ChannelId, Symbol = data.Symbol }; ;
                    }
                    else
                    {
                        existing.Name = data.ChannelId;
                        existing.Symbol = data.Symbol;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnUnsubscribe(Messages.OrderBookUnsubscribing data)
        {
            try
            {
                lock (channelLocker)
                {
                    ChannelList.Remove(data.ChannelId);
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnSnapshot(string channelId, string[][] entries)
        {
            try
            {
                Channel channel = ChannelList[channelId];
                var symbol = _symbolMapper.GetLeanSymbol(channel.Symbol);

                OrderBook orderBook;
                if (!_orderBooks.TryGetValue(symbol, out orderBook))
                {
                    orderBook = new OrderBook(symbol);
                    _orderBooks[symbol] = orderBook;
                }
                else
                {
                    orderBook.BestBidAskUpdated -= OnBestBidAskUpdated;
                    orderBook.Clear();
                }

                foreach (var entry in entries)
                {
                    var price = decimal.Parse(entry[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                    var amount = decimal.Parse(entry[2], NumberStyles.Float, CultureInfo.InvariantCulture);

                    if (amount > 0)
                        orderBook.UpdateBidRow(price, amount);
                    else
                        orderBook.UpdateAskRow(price, amount);
                }

                orderBook.BestBidAskUpdated += OnBestBidAskUpdated;

                EmitQuoteTick(symbol, orderBook.BestBidPrice, orderBook.BestBidSize, orderBook.BestAskPrice, orderBook.BestAskSize);
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnUpdate(string channelId, string[] entries)
        {
            try
            {
                Channel channel = ChannelList[channelId];
                var symbol = _symbolMapper.GetLeanSymbol(channel.Symbol);
                var orderBook = _orderBooks[symbol];

                var price = decimal.Parse(entries[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                var count = int.Parse(entries[1]);
                var amount = decimal.Parse(entries[2], NumberStyles.Float, CultureInfo.InvariantCulture);

                if (count == 0)
                {
                    orderBook.RemovePriceLevel(price);
                }
                else
                {
                    if (amount > 0)
                    {
                        orderBook.UpdateBidRow(price, amount);
                    }
                    else if (amount < 0)
                    {
                        orderBook.UpdateAskRow(price, amount);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnOrderClose(string[] entries)
        {
            string brokerId = entries[0];
            if (entries[5].IndexOf("canceled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var order = CachedOrderIDs
                    .FirstOrDefault(o => o.Value.BrokerId.Contains(brokerId))
                    .Value;
                if (order == null)
                {
                    order = _algorithm.Transactions.GetOrderByBrokerageId(brokerId);
                    if (order == null)
                    {
                        // not our order, nothing else to do here
                        return;
                    }
                }
                Order outOrder;
                if (CachedOrderIDs.TryRemove(order.Id, out outOrder))
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "Bitfinex Order Event") { Status = OrderStatus.Canceled });
                }
            }
            else
            {
                lock (closeLocker)
                {
                    _pendingClose.Add(brokerId);
                }
            }
        }

        private void EmitFillOrder(string[] entries)
        {
            try
            {
                var brokerId = entries[4];
                var order = CachedOrderIDs
                    .FirstOrDefault(o => o.Value.BrokerId.Contains(brokerId))
                    .Value;
                if (order == null)
                {
                    order = _algorithm.Transactions.GetOrderByBrokerageId(brokerId);
                    if (order == null)
                    {
                        // not our order, nothing else to do here
                        return;
                    }
                }

                var symbol = _symbolMapper.GetLeanSymbol(entries[2]);
                var fillPrice = decimal.Parse(entries[6], NumberStyles.Float, CultureInfo.InvariantCulture);
                var fillQuantity = decimal.Parse(entries[5], NumberStyles.Float, CultureInfo.InvariantCulture);
                var direction = fillQuantity < 0 ? OrderDirection.Sell : OrderDirection.Buy;
                var updTime = Time.UnixTimeStampToDateTime(double.Parse(entries[3], NumberStyles.Float, CultureInfo.InvariantCulture));
                var orderFee = 0m;
                if (fillQuantity != 0)
                {
                    var security = _securityProvider.GetSecurity(order.Symbol);
                    orderFee = security.FeeModel.GetOrderFee(security, order);
                }

                OrderStatus status = fillQuantity == order.Quantity ? OrderStatus.Filled : OrderStatus.PartiallyFilled;

                var orderEvent = new OrderEvent
                (
                    order.Id, symbol, updTime, status,
                    direction, fillPrice, fillQuantity,
                    orderFee, $"Bitfinex Order Event {direction}"
                );

                // if the order is closed, we no longer need it in the active order list
                lock (closeLocker)
                {
                    if (_pendingClose.Contains(brokerId))
                    {
                        _pendingClose.Remove(brokerId);
                        Order outOrder;
                        CachedOrderIDs.TryRemove(order.Id, out outOrder);
                    }
                }
                OnOrderEvent(orderEvent);
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnBestBidAskUpdated(object sender, BestBidAskUpdatedEventArgs e)
        {
            EmitQuoteTick(e.Symbol, e.BestBidPrice, e.BestBidSize, e.BestAskPrice, e.BestAskSize);
        }

        private void EmitQuoteTick(Symbol symbol, decimal bidPrice, decimal bidSize, decimal askPrice, decimal askSize)
        {
            lock (TickLocker)
            {
                Ticks.Add(new Tick
                {
                    AskPrice = askPrice,
                    BidPrice = bidPrice,
                    Value = (askPrice + bidPrice) / 2m,
                    Time = DateTime.UtcNow,
                    Symbol = symbol,
                    TickType = TickType.Quote,
                    AskSize = askSize,
                    BidSize = bidSize
                });
            }
        }

        /// <summary>
        /// Lock the streaming processing while we're sending orders as sometimes they fill before the REST call returns.
        /// </summary>
        public void LockStream()
        {
            Log.Trace("BItfinexBrokerage.Messaging.LockStream(): Locking Stream");
            _streamLocked = true;
        }

        /// <summary>
        /// Unlock stream and process all backed up messages.
        /// </summary>
        public void UnlockStream()
        {
            Log.Trace("BItfinexBrokerage.Messaging.UnlockStream(): Processing Backlog...");
            while (_messageBuffer.Any())
            {
                WebSocketMessage e;
                _messageBuffer.TryDequeue(out e);
                OnMessageImpl(this, e);
            }
            Log.Trace("BItfinexBrokerage.Messaging.UnlockStream(): Stream Unlocked.");
            // Once dequeued in order; unlock stream.
            _streamLocked = false;
        }
    }
}