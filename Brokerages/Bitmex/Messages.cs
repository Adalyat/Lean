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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    public class Messages
    {
        /// <summary>
        /// https://en.bitcoin.it/wiki/Satoshi_(unit)
        /// </summary>
        public const decimal Satoshi = 0.00000001m;

#pragma warning disable 1591
        public class Wallet
        {
            public string Currency { get; set; }

            [JsonProperty(PropertyName = "amount")]
            public decimal AmountInSatoshi { get; set; }

            public decimal Amount => AmountInSatoshi * Satoshi;
        }

        public class PriceTicker
        {
            public string Symbol { get; set; }

            [JsonProperty(PropertyName = "midPrice")]
            public decimal Price { get; set; }
        }

        public class Position
        {
            public string Symbol { get; set; }
            [JsonProperty("avgEntryPrice")]
            public decimal AveragePrice { get; set; }
            [JsonProperty("currentQty")]
            public decimal Quantity { get; set; }
            [JsonProperty(PropertyName = "unrealisedPnl")]
            public decimal UnrealisedPnlInSatoshi { get; set; }
            public decimal UnrealisedPnl => UnrealisedPnlInSatoshi * Satoshi;
        }

        public class Order
        {
            [JsonProperty(PropertyName = "orderID")]
            public Guid Id { get; set; }
            public string Symbol { get; set; }
            [JsonProperty(PropertyName = "ordType")]
            public string Type { get; set; }
            [JsonProperty(PropertyName = "ordStatus")]
            public string Status { get; set; }
            public decimal? Price { get; set; }
            [JsonProperty(PropertyName = "stopPx")]
            public decimal? StopPrice { get; internal set; }
            [JsonProperty(PropertyName = "orderQty")]
            public decimal Quantity { get; set; }
            public string Side { get; set; }
            public DateTime Timestamp { get; set; }
            public string Error { get; set; }
        }

        public enum EventType
        {
            None,
            Subscribe,
            Unsubscribe
        }

        public class BaseMessage
        {
            public virtual EventType Type { get; } = EventType.None;

            protected JObject JObject { get; set; }

            public BaseMessage(string content)
            {
                JObject = JObject.Parse(content);
            }

            public static BaseMessage Parse(string content)
            {
                var jobject = JObject.Parse(content);
                JToken t;
                if (jobject.TryGetValue("subscribe", out t))
                {
                    return new Subscribe(content);
                }
                if (jobject.TryGetValue("unsubscribe", out t))
                {
                    return new Unsubscribe(content);
                }
                return null;
            }

            public T ToObject<T>() where T : BaseMessage
            {
                return (T)Convert.ChangeType(this, typeof(T));
            }
        }

        public class Subscribe : BaseMessage
        {
            public override EventType Type => EventType.Subscribe;
            public string Channel => JObject.Value<string>("subscribe");
            public bool Success => JObject.Value<bool>("success");

            public Subscribe(string content) : base(content)
            { }
        }

        public class Unsubscribe : BaseMessage
        {
            public override EventType Type => EventType.Unsubscribe;
            public string Channel => JObject.Value<string>("subscribe");
            public bool Success => JObject.Value<bool>("success");

            public Unsubscribe(string content) : base(content)
            { }
        }
#pragma warning restore 1591
    }
}
