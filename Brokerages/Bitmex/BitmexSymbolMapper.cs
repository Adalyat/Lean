/*
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Brokerages.Bitmex
{
    /// <summary>
    /// Provides the mapping between Lean symbols and Bitmex symbols.
    /// </summary>
    public class BitmexSymbolMapper : ISymbolMapper
    {
        /// <summary>
        /// Symbols that are both active and delisted
        /// </summary>
        public static List<Symbol> KnownSymbols
        {
            get
            {
                var symbols = new List<Symbol>();
                var mapper = new BitmexSymbolMapper();
                foreach (var tp in KnownSymbolStrings)
                {
                    symbols.Add(mapper.GetLeanSymbol(tp, mapper.GetBrokerageSecurityType(tp), Market.Bitmex));
                }
                return symbols;
            }
        }

        /// <summary>
        /// The list of known Bitmex symbols.
        /// </summary>
        public static readonly HashSet<string> KnownSymbolStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "XBTUSD", "XBTJPY", "ETHXBT", "XBTKRW"
        };

        /// <summary>
        /// The list of known Bitmex currencies.
        /// </summary>
        private static readonly HashSet<string> KnownCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "USD", "JPY"
        };

        /// <summary>
        /// Converts a Lean symbol instance to an Bitmex symbol
        /// </summary>
        /// <param name="symbol">A Lean symbol instance</param>
        /// <returns>The Bitmex symbol</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            if (symbol == null || string.IsNullOrWhiteSpace(symbol.Value))
                throw new ArgumentException("Invalid symbol: " + (symbol == null ? "null" : symbol.ToString()));

            if (symbol.ID.SecurityType != SecurityType.Crypto)
                throw new ArgumentException("Invalid security type: " + symbol.ID.SecurityType);

            if (symbol.ID.Market != Market.Bitmex)
                throw new ArgumentException($"Invalid market: {symbol.ID.Market}");

            var brokerageSymbol = ConvertLeanSymbolToBitmexSymbol(symbol.Value);

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException("Unknown symbol: " + symbol.Value);

            return brokerageSymbol;
        }

        /// <summary>
        /// Converts an Bitmex symbol to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The Bitmex symbol</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">Expiration date of the security(if applicable)</param>
        /// <param name="strike">The strike of the security (if applicable)</param>
        /// <param name="optionRight">The option right of the security (if applicable)</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = 0)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                throw new ArgumentException($"Invalid Bitmex symbol: {brokerageSymbol}");

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException($"Unknown Bitmex symbol: {brokerageSymbol}");

            if (securityType != SecurityType.Crypto)
                throw new ArgumentException($"Invalid security type: {securityType}");

            if (market != Market.Bitmex)
                throw new ArgumentException($"Invalid market: {market}");

            return Symbol.Create(ConvertBitmexSymbolToLeanSymbol(brokerageSymbol), GetBrokerageSecurityType(brokerageSymbol), Market.Bitmex);
        }

        /// <summary>
        /// Converts an Bitmex symbol to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The Bitmex symbol</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string brokerageSymbol)
        {
            var securityType = GetBrokerageSecurityType(brokerageSymbol);
            return GetLeanSymbol(brokerageSymbol, securityType, Market.Bitmex);
        }

        /// <summary>
        /// Returns the security type for an Bitmex symbol
        /// </summary>
        /// <param name="brokerageSymbol">The Bitmex symbol</param>
        /// <returns>The security type</returns>
        public SecurityType GetBrokerageSecurityType(string brokerageSymbol)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                throw new ArgumentException($"Invalid Bitmex symbol: {brokerageSymbol}");

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException($"Unknown Bitmex symbol: {brokerageSymbol}");

            return SecurityType.Crypto;
        }

        /// <summary>
        /// Returns the security type for a Lean symbol
        /// </summary>
        /// <param name="leanSymbol">The Lean symbol</param>
        /// <returns>The security type</returns>
        public SecurityType GetLeanSecurityType(string leanSymbol)
        {
            return GetBrokerageSecurityType(ConvertLeanSymbolToBitmexSymbol(leanSymbol));
        }

        /// <summary>
        /// Checks if the symbol is supported by Bitmex
        /// </summary>
        /// <param name="brokerageSymbol">The Bitmex symbol</param>
        /// <returns>True if Bitmex supports the symbol</returns>
        public bool IsKnownBrokerageSymbol(string brokerageSymbol)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                return false;

            return KnownSymbolStrings.Contains(brokerageSymbol);
        }

        /// <summary>
        /// Checks if the currency is supported by Bitmex
        /// </summary>
        /// <returns>True if Bitmex supports the currency</returns>
        public bool IsKnownFiatCurrency(string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
                return false;

            return KnownCurrencies.Contains(currency);
        }

        /// <summary>
        /// Checks if the symbol is supported by Bitmex
        /// </summary>
        /// <param name="symbol">The Lean symbol</param>
        /// <returns>True if Bitmex supports the symbol</returns>
        public bool IsKnownLeanSymbol(Symbol symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol?.Value) || symbol.Value.Length <= 3)
                return false;

            var BitmexSymbol = ConvertLeanSymbolToBitmexSymbol(symbol.Value);

            return IsKnownBrokerageSymbol(BitmexSymbol) && GetBrokerageSecurityType(BitmexSymbol) == symbol.ID.SecurityType;
        }

        /// <summary>
        /// Converts an Bitmex symbol to a Lean symbol string
        /// </summary>
        private static string ConvertBitmexSymbolToLeanSymbol(string BitmexSymbol)
        {
            if (string.IsNullOrWhiteSpace(BitmexSymbol))
                throw new ArgumentException($"Invalid Bitmex symbol: {BitmexSymbol}");

            // return as it is due to Bitmex has similar Symbol format
            return BitmexSymbol.ToUpper();
        }

        /// <summary>
        /// Converts a Lean symbol string to an Bitmex symbol
        /// </summary>
        private static string ConvertLeanSymbolToBitmexSymbol(string leanSymbol)
        {
            if (string.IsNullOrWhiteSpace(leanSymbol))
                throw new ArgumentException($"Invalid Lean symbol: {leanSymbol}");

            // return as it is due to Bitmex has similar Symbol format
            return leanSymbol.ToUpper();
        }
    }
}
