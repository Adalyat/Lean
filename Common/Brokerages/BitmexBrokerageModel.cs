using QuantConnect.Orders.Fees;
using QuantConnect.Securities;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Brokerages
{
    /// <summary>
    /// Provides Bitmex specific properties
    /// </summary>
    public class BitmexBrokerageModel : DefaultBrokerageModel
    {
        /// <summary>
        /// Gets a map of the default markets to be used for each security type
        /// </summary>
        public override IReadOnlyDictionary<SecurityType, string> DefaultMarkets { get; } = GetDefaultMarkets();

        /// <summary>
        /// Initializes a new instance of the <see cref="BitmexBrokerageModel"/> class
        /// </summary>
        /// <param name="accountType">The type of account to be modelled, defaults to <see cref="AccountType.Margin"/></param>
        public BitmexBrokerageModel(AccountType accountType = AccountType.Margin) : base(accountType)
        {
            if (accountType == AccountType.Cash)
            {
                throw new Exception("The Bitmex brokerage does not currently support Cash trading.");
            }
        }

        /// <summary>
        /// Gets a new buying power model for the security, returning the default model with the security's configured leverage.
        /// </summary>
        /// <param name="security">The security to get a buying power model for</param>
        /// <returns>The buying power model for this brokerage/security</returns>
        public override IBuyingPowerModel GetBuyingPowerModel(Security security)
        {
            return new SecurityMarginModel(GetLeverage(security));
        }

        /// <summary>
        /// Provides Bitmex fee model
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public override IFeeModel GetFeeModel(Security security)
        {
            return new BitmexFeeModel();
        }

        /// <summary>
        /// Bitmex global leverage rule
        /// https://www.bitmex.com/app/fees
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public override decimal GetLeverage(Security security)
        {
            if (security.Type != SecurityType.Crypto)
            {
                throw new Exception($"Invalid security type: {security.Type}"); ;
            }

            switch (security.QuoteCurrency.Symbol)
            {
                case "XBT":
                    //return 100m;
                case "ETH":
                    //return 50m;
                default:
                    return 1m;
            }
        }

        private static IReadOnlyDictionary<SecurityType, string> GetDefaultMarkets()
        {
            var map = DefaultMarketMap.ToDictionary();
            map[SecurityType.Crypto] = Market.Bitmex;
            return map.ToReadOnlyDictionary();
        }
    }
}
