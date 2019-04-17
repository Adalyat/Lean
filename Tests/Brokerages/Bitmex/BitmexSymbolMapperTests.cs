using NUnit.Framework;
using QuantConnect.Brokerages.Bitmex;
using System;

namespace QuantConnect.Tests.Brokerages.Bitmex
{
    [TestFixture]
    public class BitmexSymbolMapperTests
    {
        private BitmexSymbolMapper mapper;

        [SetUp]
        public void Setup()
        {
            mapper = new BitmexSymbolMapper();
        }

        #region data
        public TestCaseData[] CryptoPairs => new[]
        {
            new TestCaseData("xbtjpy"),
            new TestCaseData("xbtusd"),
            new TestCaseData("ethxbt")
        };

        public TestCaseData[] CryptoSymbols => new[]
        {
            new TestCaseData(Symbol.Create("XBTJPY", SecurityType.Crypto, Market.Bitmex)),
            new TestCaseData(Symbol.Create("XBTUSD", SecurityType.Crypto, Market.Bitmex)),
            new TestCaseData(Symbol.Create("ETHXBT", SecurityType.Crypto, Market.Bitmex))
        };

        public TestCaseData[] CurrencyPairs => new[]
        {
            new TestCaseData(""),
            new TestCaseData("eurusd"),
            new TestCaseData("gbpusd"),
            new TestCaseData("usdjpy")
        };

        public TestCaseData[] UnknownSymbols => new[]
        {
            new TestCaseData("eth-usd", SecurityType.Crypto, Market.Bitmex),
            new TestCaseData("BTC/USD", SecurityType.Crypto, Market.Bitmex),
            new TestCaseData("eurusd", SecurityType.Crypto, Market.GDAX),
            new TestCaseData("gbpusd", SecurityType.Forex, Market.Bitmex),
            new TestCaseData("usdjpy", SecurityType.Forex, Market.FXCM),
            new TestCaseData("btceth", SecurityType.Crypto, Market.Bitmex)
        };

        public TestCaseData[] UnknownSecurityType => new[]
        {
            new TestCaseData("XBTUSD", SecurityType.Forex, Market.Bitmex),
        };

        public TestCaseData[] UnknownMarket => new[]
        {
            new TestCaseData("ethxbt", SecurityType.Crypto, Market.GDAX)
        };

        #endregion

        [Test]
        [TestCaseSource("CryptoPairs")]
        public void ReturnsCorrectLeanSymbol(string pair)
        {
            var symbol = mapper.GetLeanSymbol(pair);
            Assert.AreEqual(pair.ToUpper(), symbol.Value);
            Assert.AreEqual(SecurityType.Crypto, symbol.ID.SecurityType);
            Assert.AreEqual(Market.Bitmex, symbol.ID.Market);
        }

        [Test]
        [TestCaseSource("CryptoSymbols")]
        public void ReturnsCorrectBrokerageSymbol(Symbol symbol)
        {
            Assert.AreEqual(symbol.Value.ToUpper(), mapper.GetBrokerageSymbol(symbol));
        }

        [Test]
        [TestCaseSource("CurrencyPairs")]
        public void ThrowsOnCurrencyPairs(string pair)
        {
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSecurityType(pair));
        }

        [Test]
        public void ThrowsOnNullOrEmptySymbols()
        {
            string ticker = null;
            Assert.IsFalse(mapper.IsKnownBrokerageSymbol(ticker));
            Assert.Throws<ArgumentException>(() => mapper.GetLeanSymbol(ticker, SecurityType.Crypto, Market.Bitmex));
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSecurityType(ticker));

            ticker = "";
            Assert.IsFalse(mapper.IsKnownBrokerageSymbol(ticker));
            Assert.Throws<ArgumentException>(() => mapper.GetLeanSymbol(ticker, SecurityType.Crypto, Market.Bitmex));
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSecurityType(ticker));
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSymbol(Symbol.Create(ticker, SecurityType.Crypto, Market.Bitmex)));
        }

        [Test]
        [TestCaseSource("UnknownSymbols")]
        public void ThrowsOnUnknownSymbols(string pair, SecurityType type, string market)
        {
            Assert.IsFalse(mapper.IsKnownBrokerageSymbol(pair));
            Assert.Throws<ArgumentException>(() => mapper.GetLeanSymbol(pair, type, market));
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSymbol(Symbol.Create(pair, type, market)));
        }

        [Test]
        [TestCaseSource("UnknownMarket")]
        [TestCaseSource("UnknownSecurityType")]
        public void ThrowsOnUnknownMetas(string pair, SecurityType type, string market)
        {
            Assert.IsTrue(mapper.IsKnownBrokerageSymbol(pair));
            Assert.Throws<ArgumentException>(() => mapper.GetLeanSymbol(pair, type, market));
            Assert.Throws<ArgumentException>(() => mapper.GetBrokerageSymbol(Symbol.Create(pair, type, market)));
        }
    }
}
