using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Util;
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.Bitmex
{
    /// <summary>
    /// Factory method to create Bitmex Websockets brokerage
    /// </summary>
    public class BitmexBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Factory constructor
        /// </summary>
        public BitmexBrokerageFactory() : base(typeof(BitmexBrokerage))
        {
        }

        /// <summary>
        /// Not required
        /// </summary>
        public override void Dispose()
        {
        }

        /// <summary>
        /// provides brokerage connection data
        /// </summary>
        public override Dictionary<string, string> BrokerageData => new Dictionary<string, string>
        {
            { "bitmex-rest" , Config.Get("bitmex-rest", "https://www.bitmex.com")},
            { "bitmex-wss" , Config.Get("bitmex-wss", "wss://www.bitmex.com/realtime")},
            { "bitmex-api-key", Config.Get("bitmex-api-key")},
            { "bitmex-api-secret", Config.Get("bitmex-api-secret")}
        };

        /// <summary>
        /// The brokerage model
        /// </summary>
        public override IBrokerageModel BrokerageModel => new BitmexBrokerageModel();

        /// <summary>
        /// Create the Brokerage instance
        /// </summary>
        /// <param name="job"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        public override IBrokerage CreateBrokerage(Packets.LiveNodePacket job, IAlgorithm algorithm)
        {
            var required = new[] { "bitmex-rest", "bitmex-wss", "bitmex-api-secret", "bitmex-api-key" };

            foreach (var item in required)
            {
                if (string.IsNullOrEmpty(job.BrokerageData[item]))
                    throw new Exception($"BitmexBrokerageFactory.CreateBrokerage: Missing {item} in config.json");
            }

            var priceProvider = new ApiPriceProvider(job.UserId, job.UserToken);

            var brokerage = new BitmexBrokerage(
                job.BrokerageData["bitmex-wss"],
                job.BrokerageData["bitmex-rest"],
                job.BrokerageData["bitmex-api-key"],
                job.BrokerageData["bitmex-api-secret"],
                algorithm,
                priceProvider);
            Composer.Instance.AddPart<IDataQueueHandler>(brokerage);

            return brokerage;
        }
    }
}
