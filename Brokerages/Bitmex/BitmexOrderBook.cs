using QuantConnect.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    /// <summary>
    /// Represents a full order book for a Bitmex security.
    /// </summary>
    public class BitmexOrderBook : OrderBook
    {
        /// <summary>
        /// id on an orderBookL2 entry is a composite of price and symbol, and is always unique for any given price level
        /// </summary>
        public Dictionary<long, PriceLevelEntry> PriceLevels = new Dictionary<long, PriceLevelEntry>();

        public BitmexOrderBook(Symbol symbol) : base(symbol)
        { }

        /// <summary>
        /// Add a price level from the order book
        /// </summary>
        /// <param name="priceId">bitmex price id</param>
        /// <param name="side">bitmex direction</param>
        /// <param name="price">bitmex price</param>
        public void AddPriceLevel(long priceId, OrderDirection side, decimal price)
        {
            if (!PriceLevels.ContainsKey(priceId))
            {
                PriceLevels[priceId] = new PriceLevelEntry() { Side = side, Price = price };
            }
        }

        /// <summary>
        /// Ger a price level from the order book
        /// </summary>
        /// <param name="priceId">bitmex price id</param>
        public PriceLevelEntry GetPriceLevel(long priceId)
        {
            return PriceLevels[priceId];
        }

        /// <summary>
        /// Removes a price level from the order book
        /// </summary>
        /// <param name="priceId">bitmex price id</param>
        public void RemovePriceLevel(long priceId)
        {
            var price = PriceLevels[priceId];
            RemoveAskRow(price.Price);
            RemoveBidRow(price.Price);
            PriceLevels.Remove(priceId);
        }

        /// <summary>
        /// Clears all bid/ask levels and prices.
        /// </summary>
        public void Clear()
        {
            base.Clear();
            PriceLevels.Clear();
        }

        public class PriceLevelEntry
        {
            public OrderDirection Side { get; set; }
            public decimal Price { get; set; }
        }
    }
}
