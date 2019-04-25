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

using QuantConnect.Orders;
using QuantConnect.Orders.Fees;

namespace QuantConnect.Securities.Crypto
{
    /// <summary>
    /// Crypto holdings implementation of the base securities class
    /// </summary>
    /// <seealso cref="SecurityHolding"/>
    public class BitmexHolding : CryptoHolding
    {
        /// <summary>
        /// Crypto Holding Class
        /// </summary>
        /// <param name="security">The Crypto security being held</param>
        /// <param name="currencyConverter">A currency converter instance</param>
        public BitmexHolding(Crypto security, ICurrencyConverter currencyConverter)
            : base(security, currencyConverter)
        {
        }

        public override decimal TotalCloseProfit()
        {
            if (Quantity == 0)
            {
                return 0;
            }

            // this is in the account currency
            var marketOrder = new MarketOrder(
                Security.Symbol, 
                -Quantity, 
                Security.LocalTime.ConvertToUtc(Security.Exchange.TimeZone));

            var orderFee = Security.FeeModel.GetOrderFee(
                new OrderFeeParameters(Security, marketOrder)).Value;
            var feesInAccountCurrency = CurrencyConverter.
                ConvertToAccountCurrency(orderFee).Amount;

            var price = marketOrder.Direction == OrderDirection.Sell ? Security.BidPrice : Security.AskPrice;

            return (price - AveragePrice) / price * Quantity * Security.QuoteCurrency.ConversionRate
                * Security.SymbolProperties.ContractMultiplier - feesInAccountCurrency;
        }
    }
}