using Newtonsoft.Json.Linq;
using QuantConnect.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitmex
{
    public partial class BitmexBrokerage
    {
        private volatile bool _streamLocked;
        private readonly object TickLocker = new object();
        private readonly object channelLocker = new object();
        private readonly ConcurrentQueue<WebSocketMessage> _messageBuffer = new ConcurrentQueue<WebSocketMessage>();

        /// <summary>
        /// Wss message handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public override void OnMessage(object sender, WebSocketMessage e)
        {
            LastHeartbeatUtcTime = DateTime.UtcNow;

            // Verify if we're allowed to handle the streaming packet yet; while we're placing an order we delay the
            // stream processing a touch.
            try
            {
                if (_streamLocked)
                {
                    _messageBuffer.Enqueue(e);
                    return;
                }
            }
            catch (Exception err)
            {
                Log.Error(err);
            }

            OnMessageImpl(sender, e);
        }

        /// <summary>
        /// Implementation of the OnMessage event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMessageImpl(object sender, WebSocketMessage e)
        {
            try
            {
                var message = Messages.BaseMessage.Parse(e.Message);
                switch (message?.Type)
                {
                    case Messages.EventType.Subscribe:
                        OnSubscribe(message.ToObject<Messages.Subscribe>());
                        return;
                    case Messages.EventType.Unsubscribe:
                        OnUnsubscribe(message.ToObject<Messages.Unsubscribe>());
                        return;
                }
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Parsing wss message failed. Data: {e.Message} Exception: {exception}"));
                throw;
            }
        }

        /// <summary>
        /// Lock the streaming processing while we're sending orders as sometimes they fill before the REST call returns.
        /// </summary>
        private void LockStream()
        {
            Log.Trace("BitmexBrokerage.Messaging.LockStream(): Locking Stream");
            _streamLocked = true;
        }

        /// <summary>
        /// Unlock stream and process all backed up messages.
        /// </summary>
        private void UnlockStream()
        {
            Log.Trace("BitmexBrokerage.Messaging.UnlockStream(): Processing Backlog...");
            while (_messageBuffer.Any())
            {
                WebSocketMessage e;
                _messageBuffer.TryDequeue(out e);
                OnMessageImpl(this, e);
            }
            Log.Trace("BitmexBrokerage.Messaging.UnlockStream(): Stream Unlocked.");
            // Once dequeued in order; unlock stream.
            _streamLocked = false;
        }

        private void OnSubscribe(Messages.Subscribe v)
        {
            try
            {
                string[] subscription = v.Channel.Split(':');
                lock (channelLocker)
                {
                    if (!ChannelList.ContainsKey(v.Channel))
                    {
                        ChannelList.Add(v.Channel, new Channel() { Name = subscription[0], Symbol = subscription[1] });
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnUnsubscribe(Messages.Unsubscribe v)
        {
            try
            {
                lock (channelLocker)
                {
                    if (ChannelList.ContainsKey(v.Channel))
                    {
                        ChannelList.Remove(v.Channel);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }
    }
}
