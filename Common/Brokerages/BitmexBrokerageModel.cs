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
    }
}
