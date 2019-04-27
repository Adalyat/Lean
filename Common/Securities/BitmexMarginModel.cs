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
using System;

namespace QuantConnect.Securities
{
    /// <summary>
    /// Represents a simple, constant margining model by specifying the percentages of required margin.
    /// </summary>
    public class BitmexMarginModel : SecurityMarginModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityMarginModel"/>
        /// </summary>
        /// <param name="initialMarginRequirement">The percentage of an order's absolute cost
        /// that must be held in free cash in order to place the order</param>
        /// <param name="maintenanceMarginRequirement">The percentage of the holding's absolute
        /// cost that must be held in free cash in order to avoid a margin call</param>
        /// <param name="requiredFreeBuyingPowerPercent">The percentage used to determine the required
        /// unused buying power for the account.</param>
        public BitmexMarginModel(
            decimal initialMarginRequirement,
            decimal maintenanceMarginRequirement,
            decimal requiredFreeBuyingPowerPercent
            )
            : base(initialMarginRequirement, maintenanceMarginRequirement, requiredFreeBuyingPowerPercent)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityMarginModel"/>
        /// </summary>
        /// <param name="leverage">The leverage</param>
        /// <param name="requiredFreeBuyingPowerPercent">The percentage used to determine the required
        /// unused buying power for the account.</param>
        public BitmexMarginModel(decimal leverage, decimal requiredFreeBuyingPowerPercent = 0)
            : base(leverage, requiredFreeBuyingPowerPercent)
        {
        }

        protected override decimal GetInitialMarginRequiredForOrder(InitialMarginRequiredForOrderParameters parameters)
        {
            var fees = parameters.Security.FeeModel.GetOrderFee(
                new OrderFeeParameters(parameters.Security,
                    parameters.Order)).Value;
            var feesInAccountCurrency = parameters.CurrencyConverter.
                ConvertToAccountCurrency(fees).Amount;

            var orderValue = parameters.Order.AbsoluteQuantity
                * GetInitialMarginRequirement(parameters.Security);
            return orderValue + Math.Sign(orderValue) * feesInAccountCurrency;
        }

        public override GetMaximumOrderQuantityForTargetValueResult GetMaximumOrderQuantityForTargetValue(GetMaximumOrderQuantityForTargetValueParameters parameters)
        {
            // this is expensive so lets fetch it once
            var totalPortfolioValue = parameters.Portfolio.TotalPortfolioValue;

            // adjust target portfolio value to comply with required Free Buying Power Percent
            var targetPortfolioValue =
                parameters.Target * (totalPortfolioValue - totalPortfolioValue * RequiredFreeBuyingPowerPercent);

            // if targeting zero, simply return the negative of the quantity
            if (targetPortfolioValue == 0)
            {
                return new GetMaximumOrderQuantityForTargetValueResult(-parameters.Security.Holdings.Quantity, string.Empty, false);
            }

            var currentHoldingsValue = parameters.Security.Holdings.HoldingsValue;

            // remove directionality, we'll work in the land of absolutes
            var targetOrderValue = Math.Abs(targetPortfolioValue - currentHoldingsValue);
            var direction = targetPortfolioValue > currentHoldingsValue ? OrderDirection.Buy : OrderDirection.Sell;

            // determine the unit price in terms of the account currency
            var utcTime = parameters.Security.LocalTime.ConvertToUtc(parameters.Security.Exchange.TimeZone);
            var unitPrice = new MarketOrder(parameters.Security.Symbol, 1, utcTime).GetValue(parameters.Security);
            if (unitPrice == 0)
            {
                var reason = $"The price of the {parameters.Security.Symbol.Value} security is zero because it does not have any market " +
                    "data yet. When the security price is set this security will be ready for trading.";
                return new GetMaximumOrderQuantityForTargetValueResult(0, reason);
            }

            // calculate the total margin available
            var marginRemaining = GetMarginRemaining(parameters.Portfolio, parameters.Security, direction);
            if (marginRemaining <= 0)
            {
                var reason = "The portfolio does not have enough margin available.";
                return new GetMaximumOrderQuantityForTargetValueResult(0, reason);
            }

            // continue iterating while we do not have enough margin for the order
            decimal orderValue = 0;
            decimal orderFees = 0;
            // compute the initial order quantity
            var orderQuantity = targetOrderValue;

            // rounding off Order Quantity to the nearest multiple of Lot Size
            orderQuantity -= orderQuantity % parameters.Security.SymbolProperties.LotSize;
            if (orderQuantity == 0)
            {
                var reason = $"The order quantity is less than the lot size of {parameters.Security.SymbolProperties.LotSize} " +
                    "and has been rounded to zero.";
                return new GetMaximumOrderQuantityForTargetValueResult(0, reason, false);
            }

            var loopCount = 0;
            // Just in case...
            var lastOrderQuantity = 0m;
            do
            {
                // Each loop will reduce the order quantity based on the difference between orderValue and targetOrderValue
                if (orderValue > targetOrderValue)
                {
                    var currentOrderValuePerUnit = orderValue / orderQuantity;
                    var amountOfOrdersToRemove = (orderValue - targetOrderValue) / currentOrderValuePerUnit;
                    if (amountOfOrdersToRemove < parameters.Security.SymbolProperties.LotSize)
                    {
                        // we will always substract at leat 1 LotSize
                        amountOfOrdersToRemove = parameters.Security.SymbolProperties.LotSize;
                    }

                    orderQuantity -= amountOfOrdersToRemove;
                    orderQuantity -= orderQuantity % parameters.Security.SymbolProperties.LotSize;
                }

                if (orderQuantity <= 0)
                {
                    var reason = $"The order quantity is less than the lot size of {parameters.Security.SymbolProperties.LotSize} " +
                        $"and has been rounded to zero.Target order value {targetOrderValue}. Order fees " +
                        $"{orderFees}. Order quantity {orderQuantity}.";
                    return new GetMaximumOrderQuantityForTargetValueResult(0, reason);
                }

                // generate the order
                var order = new MarketOrder(parameters.Security.Symbol, orderQuantity, utcTime);

                var fees = parameters.Security.FeeModel.GetOrderFee(
                    new OrderFeeParameters(parameters.Security,
                        order)).Value;
                orderFees = parameters.Portfolio.CashBook.ConvertToAccountCurrency(fees).Amount;

                // The TPV, take out the fees(unscaled) => yields available value for trading(less fees)
                // then scale that by the target -- finally remove currentHoldingsValue to get targetOrderValue
                targetOrderValue = Math.Abs(
                    (totalPortfolioValue - orderFees - totalPortfolioValue * RequiredFreeBuyingPowerPercent)
                    * parameters.Target - currentHoldingsValue
                );

                // After the first loop we need to recalculate order quantity since now we have fees included
                if (loopCount == 0)
                {
                    // re compute the initial order quantity
                    orderQuantity = targetOrderValue;
                    orderQuantity -= orderQuantity % parameters.Security.SymbolProperties.LotSize;
                }
                else
                {
                    // Start safe check after first loop
                    if (lastOrderQuantity == orderQuantity)
                    {
                        var message = "GetMaximumOrderQuantityForTargetValue failed to converge to target order value " +
                            $"{targetOrderValue}. Current order value is {orderValue}. Order quantity {orderQuantity}. " +
                            $"Lot size is {parameters.Security.SymbolProperties.LotSize}. Order fees {orderFees}. Security symbol " +
                            $"{parameters.Security.Symbol}";
                        throw new Exception(message);
                    }

                    lastOrderQuantity = orderQuantity;
                }

                orderValue = orderQuantity;
                loopCount++;
                // we always have to loop at least twice
            }
            while (loopCount < 2 || orderValue > targetOrderValue);

            // add directionality back in
            return new GetMaximumOrderQuantityForTargetValueResult((direction == OrderDirection.Sell ? -1 : 1) * orderQuantity);
        }
    }
}