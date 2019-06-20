using Newtonsoft.Json.Linq;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    public partial class BitmexBrokerage
    {
        private volatile bool _streamLocked;
        private readonly object TickLocker = new object();
        private readonly object channelLocker = new object();
        private readonly ConcurrentQueue<WebSocketMessage> _messageBuffer = new ConcurrentQueue<WebSocketMessage>();
        private readonly ConcurrentDictionary<Symbol, BitmexOrderBook> _orderBooks = new ConcurrentDictionary<Symbol, BitmexOrderBook>();

        /// <summary>
        /// Wss message handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public override void OnMessage(object sender, WebSocketMessage e)
        {
            LastHeartbeatUtcTime = DateTime.UtcNow;

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
        /// Implementation of the OnMessage event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMessageImpl(object sender, WebSocketMessage e)
        {
            try
            {
                var message = Messages.BaseMessage.Parse(e.Message);
                switch (message?.Type)
                {
                    case Messages.EventType.Subscribe:
                        OnSubscribe(message.ToObject<Messages.Subscribe>());
                        return;
                    case Messages.EventType.Unsubscribe:
                        OnUnsubscribe(message.ToObject<Messages.Unsubscribe>());
                        return;
                    case Messages.EventType.OrderBook:
                        OnOrderbook(message.ToObject<Messages.OrderBookData>());
                        return;
                    case Messages.EventType.Trade:
                        OnTrade(message.ToObject<Messages.TradeData>());
                        return;
                    case Messages.EventType.Execution:
                        OnExecution(message.ToObject<Messages.ExecutionData>());
                        return;
                }
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Parsing wss message failed. Data: {e.Message} Exception: {exception}"));
                throw;
            }
        }

        /// <summary>
        /// Lock the streaming processing while we're sending orders as sometimes they fill before the REST call returns.
        /// </summary>
        private void LockStream()
        {
            Log.Trace("BitmexBrokerage.Messaging.LockStream(): Locking Stream");
            _streamLocked = true;
        }

        /// <summary>
        /// Unlock stream and process all backed up messages.
        /// </summary>
        private void UnlockStream()
        {
            Log.Trace("BitmexBrokerage.Messaging.UnlockStream(): Processing Backlog...");
            while (_messageBuffer.Any())
            {
                WebSocketMessage e;
                _messageBuffer.TryDequeue(out e);
                OnMessageImpl(this, e);
            }
            Log.Trace("BitmexBrokerage.Messaging.UnlockStream(): Stream Unlocked.");
            // Once dequeued in order; unlock stream.
            _streamLocked = false;
        }

        private void OnSubscribe(Messages.Subscribe v)
        {
            try
            {
                string[] subscription = v.Channel.Split(':');
                lock (channelLocker)
                {
                    if (!ChannelList.ContainsKey(v.Channel))
                    {
                        ChannelList.Add(v.Channel, new Channel() { Name = subscription[0], Symbol = subscription[1] });
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnUnsubscribe(Messages.Unsubscribe v)
        {
            try
            {
                lock (channelLocker)
                {
                    if (ChannelList.ContainsKey(v.Channel))
                    {
                        ChannelList.Remove(v.Channel);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnOrderbook(Messages.OrderBookData orderBookData)
        {
            if (orderBookData.Action.Equals("partial", StringComparison.OrdinalIgnoreCase))
            {
                ProcessSnapshot(orderBookData.Data);
            }
            else
            {
                ProcessUpdate(orderBookData.Action, orderBookData.Data);
            }
        }

        private void ProcessSnapshot(IEnumerable<Messages.OrderBookEntry> entries)
        {
            try
            {
                var symbol = _symbolMapper.GetLeanSymbol(entries.First().Symbol);

                BitmexOrderBook orderBook;
                if (!_orderBooks.TryGetValue(symbol, out orderBook))
                {
                    orderBook = new BitmexOrderBook(symbol);
                    _orderBooks[symbol] = orderBook;
                }
                else
                {
                    orderBook.BestBidAskUpdated -= OnBestBidAskUpdated;
                    orderBook.Clear();
                }

                foreach (var entry in entries)
                {
                    orderBook.AddPriceLevel(entry.Id, entry.Side, entry.Price);

                    if (entry.Side == Orders.OrderDirection.Buy)
                        orderBook.UpdateBidRow(entry.Price, entry.Size);
                    else
                        orderBook.UpdateAskRow(entry.Price, entry.Size);
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

        private void ProcessUpdate(string action, IEnumerable<Messages.OrderBookEntry> entries)
        {
            try
            {
                foreach (var entry in entries)
                {
                    var symbol = _symbolMapper.GetLeanSymbol(entry.Symbol);
                    var orderBook = _orderBooks[symbol];

                    if (action == "delete")
                    {
                        orderBook.RemovePriceLevel(entry.Id);
                    }
                    else
                    {
                        if (action == "insert")
                        {
                            orderBook.AddPriceLevel(entry.Id, entry.Side, entry.Price);
                        }

                        var priceLevel = orderBook.GetPriceLevel(entry.Id);
                        if (entry.Side != priceLevel.Side)
                        {
                            orderBook.RemovePriceLevel(entry.Id);
                            orderBook.AddPriceLevel(entry.Id, entry.Side, priceLevel.Price);
                        }

                        if (entry.Side == Orders.OrderDirection.Buy)
                        {
                            orderBook.UpdateBidRow(priceLevel.Price, entry.Size);
                        }
                        else if (entry.Side == Orders.OrderDirection.Sell)
                        {
                            orderBook.UpdateAskRow(priceLevel.Price, entry.Size);
                        }
                    }
                }
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
                    AskSize = Math.Abs(askSize),
                    BidSize = Math.Abs(bidSize)
                });
            }
        }

        private void OnTrade(Messages.TradeData trade)
        {
            if (trade.Action != "insert")
                return;

            foreach (var item in trade.Data)
            {
                EmitTradeTick(
                    _symbolMapper.GetLeanSymbol(item.Symbol),
                    item.Timestamp,
                    item.Price,
                    item.Size);
            }
        }

        private void EmitTradeTick(Symbol symbol, DateTime time, decimal price, decimal quantity)
        {
            lock (TickLocker)
            {
                Ticks.Add(new Tick
                {
                    Symbol = symbol,
                    Value = price,
                    Quantity = Math.Abs(quantity),
                    Time = time,
                    TickType = TickType.Trade
                });
            }
        }

        private void OnExecution(Messages.ExecutionData data)
        {
            if (data.Action != "insert")
                return;

            foreach (var item in data.Data)
            {
                if (!string.Equals(item.ExecType, "Funding", StringComparison.OrdinalIgnoreCase))
                {
                    var status = ConvertOrderStatus(item.Status);
                    if (status == OrderStatus.PartiallyFilled || status == OrderStatus.Filled)
                    {
                        OnFillOrder(item);
                    }
                    else if (status == OrderStatus.Canceled)
                    {
                        OnOrderClose(item);
                    }
                }
                else
                {
                    OnFunding(item);
                }
            }
        }

        private void OnFillOrder(Messages.ExecutionDataEntry data)
        {
            try
            {
                if (!data.Side.HasValue)
                {
                    return;
                }

                var order = FindOrderByExternalId(data.OrderId.ToString());
                if (order == null)
                {
                    // not our order, nothing else to do here
                    return;
                }

                var symbol = _symbolMapper.GetLeanSymbol(data.Symbol);
                var fillPrice = data.LastPrice;
                var fillQuantity = data.LastQuantity * (data.Side.Value == OrderDirection.Sell ? -1 : 1);
                var updTime = data.Timestamp;
                var orderFee = new OrderFee(new CashAmount(data.Fee, data.FeeCurreny));

                var orderEvent = new OrderEvent
                (
                    order.Id, symbol, updTime, ConvertOrderStatus(data.Status),
                    data.Side.Value, fillPrice.Value, fillQuantity.Value,
                    orderFee, $"Bitmex Order Event {data.Side}"
                );

                OnOrderEvent(orderEvent);
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnOrderClose(Messages.ExecutionDataEntry data)
        {
            var order = FindOrderByExternalId(data.OrderId.ToString());
            if (order == null)
            {
                // not our order, nothing else to do here
                return;
            }

            Orders.Order outOrder;
            if (CachedOrderIDs.TryRemove(order.Id, out outOrder))
            {
                OnOrderEvent(new OrderEvent(order, data.Timestamp, OrderFee.Zero, "Bitmex Order Event") { Status = OrderStatus.Canceled });
            }
        }

        private void OnFunding(Messages.ExecutionDataEntry data)
        {
            try
            {
                _algorithm.Portfolio.CashBook["XBT"].AddAmount(-1 * (data.FeeInSatoshi * Messages.Satoshi ?? 0));
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private Orders.Order FindOrderByExternalId(string brokerId)
        {
            var order = CachedOrderIDs
                .FirstOrDefault(o => o.Value.BrokerId.Contains(brokerId))
                .Value;
            if (order == null)
            {
                order = _algorithm.Transactions.GetOrderByBrokerageId(brokerId);
            }

            return order;
        }
    }
}
