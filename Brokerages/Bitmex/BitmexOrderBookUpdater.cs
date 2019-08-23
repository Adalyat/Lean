using QuantConnect.Orders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    /// <summary>
    /// Represents a full order book updater for a Bitmex security.
    /// </summary>
    public class BitmexOrderBookUpdater : IOrderBookUpdater<long, decimal>
    {
        private decimal _bestBidPrice;
        private decimal _bestBidSize;
        private decimal _bestAskPrice;
        private decimal _bestAskSize;
        private readonly object _locker = new object();
        private readonly Symbol _symbol;

        /// <summary>
        /// Represents bid prices and sizes
        /// </summary>
        private readonly ConcurrentDictionary<decimal, PriceLevelEntry> _bids = new ConcurrentDictionary<decimal, PriceLevelEntry>();

        /// <summary>
        /// Represents ask prices and sizes
        /// </summary>
        private readonly ConcurrentDictionary<decimal, PriceLevelEntry> _asks = new ConcurrentDictionary<decimal, PriceLevelEntry>();

        /// <summary>
        /// The best bid price
        /// </summary>
        public decimal BestBidPrice
        {
            get
            {
                lock (_locker)
                {
                    return _bestBidPrice;
                }
            }
        }

        /// <summary>
        /// The best bid size
        /// </summary>
        public decimal BestBidSize
        {
            get
            {
                lock (_locker)
                {
                    return _bestBidSize;
                }
            }
        }

        /// <summary>
        /// The best ask price
        /// </summary>
        public decimal BestAskPrice
        {
            get
            {
                lock (_locker)
                {
                    return _bestAskPrice;
                }
            }
        }

        /// <summary>
        /// The best ask size
        /// </summary>
        public decimal BestAskSize
        {
            get
            {
                lock (_locker)
                {
                    return _bestAskSize;
                }
            }
        }

        /// <summary>
        /// Event fired each time <see cref="BestBidPrice"/> or <see cref="BestAskPrice"/> are changed
        /// </summary>
        public event EventHandler<BestBidAskUpdatedEventArgs> BestBidAskUpdated;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitmexOrderBookUpdater"/> class
        /// </summary>
        /// <param name="symbol">The symbol for the order book</param>
        public BitmexOrderBookUpdater(Symbol symbol)
        {
            _symbol = symbol;
        }

        /// <summary>
        /// Removes an ask price level from the order book
        /// </summary>
        /// <param name="priceId">The ask price level to be removed</param>
        public void RemoveAskRow(long priceId)
        {
            lock (_locker)
            {
                PriceLevelEntry outPrice;
                _asks.TryRemove(priceId, out outPrice);

                if (outPrice.Price == _bestAskPrice)
                {
                    CalcBestAskParts();

                    BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
                }
            }
        }

        /// <summary>
        /// Removes a bid price level from the order book
        /// </summary>
        /// <param name="priceId">The bid price level to be removed</param>
        public void RemoveBidRow(long priceId)
        {
            lock (_locker)
            {
                PriceLevelEntry outPrice;
                _bids.TryRemove(priceId, out outPrice);

                if (outPrice.Price == _bestBidPrice)
                {
                    CalcBestBidParts();

                    BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
                }
            }
        }

        /// <summary>
        /// Common price level removal method
        /// </summary>
        /// <param name="priceId">Bitmex price level id</param>
        public void RemovePriceLevel(long priceId)
        {
            lock (_locker)
            {
                if (_asks.ContainsKey(priceId))
                {
                    RemoveAskRow(priceId);
                }
                else if (_bids.ContainsKey(priceId))
                {
                    RemoveBidRow(priceId);
                }
            }
        }

        /// <summary>
        /// Updates or inserts an ask price level in the order book
        /// </summary>
        /// <param name="priceId">The ask price level id to be inserted or updated</param>
        /// <param name="size">The new ask price level size</param>
        public void UpdateAskRow(long priceId, decimal size)
        {
            lock (_locker)
            {
                PriceLevelEntry priceLevel;

                if (_asks.TryGetValue(priceId, out priceLevel))
                {
                    priceLevel.Size = size;
                    _asks[priceId] = priceLevel;
                }
                else if (_bids.TryRemove(priceId, out priceLevel))
                {
                    priceLevel.Size = size;
                    _asks[priceId] = priceLevel;

                    CalcBestAskParts();
                    CalcBestBidParts();

                    BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
                }
                else
                {
                    throw new KeyNotFoundException();
                }
            }
        }

        /// <summary>
        /// Updates or inserts a bid price level in the order book
        /// </summary>
        /// <param name="priceId">The bid price level id to be inserted or updated</param>
        /// <param name="size">The new bid price level size</param>
        public void UpdateBidRow(long priceId, decimal size)
        {
            PriceLevelEntry priceLevel;

            if (_bids.TryGetValue(priceId, out priceLevel))
            {
                priceLevel.Size = size;
                _bids[priceId] = priceLevel;
            }
            else if (_asks.TryRemove(priceId, out priceLevel))
            {
                priceLevel.Size = size;
                _bids[priceId] = priceLevel;

                CalcBestAskParts();
                CalcBestBidParts();

                BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
            }
            else
            {
                throw new KeyNotFoundException();
            }
        }

        /// <summary>
        /// Updates or inserts an ask price level in the order book
        /// </summary>
        /// <param name="priceId">The ask price level id to be inserted or updated</param>
        /// <param name="priceLevel">The new ask price level</param>
        public void UpdateAskRow(long priceId, PriceLevelEntry priceLevel)
        {
            lock (_locker)
            {
                _asks[priceId] = priceLevel;

                if (_bestAskPrice == 0 || priceLevel.Price <= _bestAskPrice)
                {
                    _bestAskPrice = priceLevel.Price;
                    _bestAskSize = priceLevel.Size;

                    BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
                }
            }
        }

        /// <summary>
        /// Updates or inserts a bid price level in the order book
        /// </summary>
        /// <param name="priceId">The bid price level id to be inserted or updated</param>
        /// <param name="priceLevel">The new bid price level</param>
        public void UpdateBidRow(long priceId, PriceLevelEntry priceLevel)
        {
            lock (_locker)
            {
                _bids[priceId] = priceLevel;

                if (_bestBidPrice == 0 || priceLevel.Price >= _bestBidPrice)
                {
                    _bestBidPrice = priceLevel.Price;
                    _bestBidSize = priceLevel.Size;

                    BestBidAskUpdated?.Invoke(this, new BestBidAskUpdatedEventArgs(_symbol, _bestBidPrice, _bestBidSize, _bestAskPrice, _bestAskSize));
                }
            }
        }

        /// <summary>
        /// Clears all bid/ask levels and prices.
        /// </summary>
        public void Clear()
        {
            lock (_locker)
            {
                _bestBidPrice = 0;
                _bestBidSize = 0;
                _bestAskPrice = 0;
                _bestAskSize = 0;

                _asks.Clear();
                _bids.Clear();
            }
        }

        private void CalcBestBidParts()
        {
            var priceLevel = _bids
                .Select(p => p.Value)
                .OrderByDescending(p => p.Price)
                .FirstOrDefault();
            _bestBidPrice = priceLevel.Price;
            _bestBidSize = priceLevel.Size;
        }

        private void CalcBestAskParts()
        {
            var priceLevel = _asks
                .Select(p => p.Value)
                .OrderBy(p => p.Price)
                .FirstOrDefault();
            _bestAskPrice = priceLevel.Price;
            _bestAskSize = priceLevel.Size;
        }

        /// <summary>
        /// Contains Bitmex price level information
        /// </summary>
        public struct PriceLevelEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PriceLevelEntry"/> class 
            /// </summary>
            /// <param name="price">Price of the price level</param>
            /// <param name="size">Srice of the price level</param>
            public PriceLevelEntry(decimal price, decimal size)
            {
                Price = price;
                Size = size;
            }

            /// <summary>
            /// Price level price
            /// </summary>
            public decimal Price { get; set; }

            /// <summary>
            /// Price level size
            /// </summary>
            public decimal Size { get; set; }
        }
    }
}
