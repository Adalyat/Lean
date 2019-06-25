using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.CSharp
{



    /*
     *
     * Algorithm
     * 
     */

    public class BitmexLiveSmaCross : QCAlgorithm
    {
        private string _symbol;
        private SimpleMovingAverage _fast;
        private SimpleMovingAverage _slow;
        private decimal _qtyPrev = 0;

        private BrokerageName _brokerageName;
        private OrderType _orderType;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {

            /*
             *
             * Set brokerage model
             * 
             */

            _brokerageName = BrokerageName.Bitmex;
            _orderType = OrderType.Market;
            var feeAmount = 0.001m;

            /*
             *
             * 
             */

            SetAccountCurrency("USD");
            SetBrokerageModel(_brokerageName, AccountType.Margin);


            string market = _brokerageName == BrokerageName.Bitfinex ? "Bitfinex" : "Bitmex";
            _symbol = _brokerageName == BrokerageName.Bitfinex ? "BTCUSD" : "XBTUSD";
            var sec = AddCrypto(_symbol, Resolution.Second, market, true, 2);

            _fast = SMA(_symbol, 6, Resolution.Second);
            _slow = SMA(_symbol, 20, Resolution.Second);

            //SetWarmup(10000);

        }

        /*
         *
         * Simple Test
         * 
         */

        public override void OnEndOfAlgorithm()
        {
            Liquidate();
        }

        //        public override void OnData(Slice slice)
        //        {
        //            //Log($"{slice[_symbol][0]}");
        //        }

        public override void OnData(Slice slice)
        {


            //Log($"{data[_symbol]} // {Portfolio.Cash}");
            //return;
            if (!_slow.IsReady || IsWarmingUp) return;

            decimal qty;

            if (_fast > _slow)
            {
                qty = 1;
            }
            else
            {
                qty = -1;
            }

            if (qty == _qtyPrev)
                return;

            Log($"===============================");
            Log($"SetHoldings: {qty} /// {_fast} -> {_slow}");
            //SetHoldings(_symbol, qty);
            MarketOrder(_symbol, qty - Securities[_symbol].Holdings.Quantity);
            Log($"Cash: {Portfolio.Cash}");
            Log($"Holdings ({_symbol}): {Securities[_symbol].Holdings.Quantity}");
            Log($"===============================");

            _qtyPrev = qty;
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            base.OnOrderEvent(orderEvent);

            Log($"OrderEvent: {orderEvent}");
            Log($"{orderEvent.Status}");
        }

        public override void OnBrokerageMessage(BrokerageMessageEvent messageEvent)
        {
            base.OnBrokerageMessage(messageEvent);
            Log($"{messageEvent.Type}: {messageEvent.Code}: {messageEvent.Message}");
        }
    }
}