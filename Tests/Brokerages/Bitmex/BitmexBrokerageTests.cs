using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using NUnit.Framework;
using QuantConnect.Brokerages.Bitmex;
using QuantConnect.Configuration;
using Moq;
using QuantConnect.Brokerages;
using QuantConnect.Tests.Common.Securities;
using QuantConnect.Orders;
using QuantConnect.Logging;
using System.Threading;

namespace QuantConnect.Tests.Brokerages.Bitmex
{
    [TestFixture, Ignore("This test requires a configured and testable Oanda practice account")]
    public partial class BitmexBrokerageTests : BrokerageTests
    {
        /// <summary>
        /// Creates the brokerage under test and connects it
        /// </summary>
        /// <param name="orderProvider"></param>
        /// <param name="securityProvider"></param>
        /// <returns></returns>
        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            var securities = new SecurityManager(new TimeKeeper(DateTime.UtcNow, new[] { TimeZones.NewYork }));
            securities.Add(Symbol, CreateSecurity(Symbol));
            var transactions = new SecurityTransactionManager(null, securities);
            transactions.SetOrderProcessor(new FakeOrderProcessor());

            var algorithm = new Mock<IAlgorithm>();
            algorithm.Setup(a => a.Transactions).Returns(transactions);
            algorithm.Setup(a => a.BrokerageModel).Returns(new BitmexBrokerageModel(AccountType.Margin));
            algorithm.Setup(a => a.Portfolio).Returns(new SecurityPortfolioManager(securities, transactions));

            var priceProvider = new Mock<IPriceProvider>();
            priceProvider.Setup(a => a.GetLastPrice(It.IsAny<Symbol>())).Returns(1.234m);

            return new BitmexBrokerage(
                    Config.Get("bitmex-wss", "wss://testnet.bitmex.com/realtime"),
                    Config.Get("bitmex-rest", "https://testnet.bitmex.com"),
                    Config.Get("bitmex-api-key"),
                    Config.Get("bitmex-api-secret"),
                    algorithm.Object,
                    priceProvider.Object
                );
        }

        /// <summary>
        /// Gets Bitmex symbol mapper
        /// </summary>
        protected ISymbolMapper SymbolMapper => new BitmexSymbolMapper();

        /// <summary>
        /// Gets the symbol to be traded, must be shortable
        /// </summary>
        protected override Symbol Symbol => Symbol.Create("XBTUSD", SecurityType.Crypto, Market.Bitmex);

        /// <summary>
        /// Gets the security type associated with the <see cref="BrokerageTests.Symbol" />
        /// </summary>
        protected override SecurityType SecurityType => SecurityType.Crypto;

        //no stop limit support in v1
        public override TestCaseData[] OrderParameters => new[]
        {
            new TestCaseData(new MarketOrderTestParameters(Symbol)).SetName("MarketOrder"),
            new TestCaseData(new LimitOrderTestParameters(Symbol, HighPrice, LowPrice)).SetName("LimitOrder"),
            new TestCaseData(new StopMarketOrderTestParameters(Symbol, HighPrice, LowPrice)).SetName("StopMarketOrder"),
            new TestCaseData(new StopLimitOrderTestParameters(Symbol, HighPrice, LowPrice)).SetName("StopLimitOrder"),
        };

        /// <summary>
        /// Gets a high price for the specified symbol so a limit sell won't fill
        /// </summary>
        protected override decimal HighPrice => 1000m;

        /// <summary>
        /// Gets a low price for the specified symbol so a limit buy won't fill
        /// </summary>
        protected override decimal LowPrice => 100m;

        /// <summary>
        /// Gets the current market price of the specified security
        /// </summary>
        protected override decimal GetAskPrice(Symbol symbol)
        {
            var prices = ((BitmexBrokerage)this.Brokerage).GetTicker(symbol);
            return prices.Price;
        }

        /// <summary>
        /// Returns wether or not the brokers order methods implementation are async
        /// </summary>
        protected override bool IsAsync() => false;

        /// <summary>
        /// Gets the default order quantity. Min order 10USD.
        /// </summary>
        protected override decimal GetDefaultQuantity() => 0.1m;
    }
}
