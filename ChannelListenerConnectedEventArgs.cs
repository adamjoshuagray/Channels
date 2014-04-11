using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Channels
{
    /// <summary>
    /// The EventArgs for when a channel has connected and the listener passes on
    /// the channel.
    /// </summary>
    public class ChannelListenerConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// The channel which has connected.
        /// This channel which can now be used.
        /// </summary>
        public MessageChannel Channel
        {
            get;
            private set;
        }
        /// <summary>
        /// Creates a new EventArgs.
        /// </summary>
        /// <param name="channel">The channel that has connected.</param>
        public ChannelListenerConnectedEventArgs(MessageChannel channel)
        {
            Channel = channel;
        }
    }
}
