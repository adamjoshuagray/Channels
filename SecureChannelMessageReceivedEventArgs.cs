using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;

namespace Channels
{
    /// <summary>
    /// The message received EventArgs for a secure channel.
    /// </summary>
    public class SecureChannelMessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The attributes in the message that was received.
        /// </summary>
        public Dictionary<string, byte[]> Attributes
        {
            get;
            private set;
        }
        /// <summary>
        /// The message context of the message that was received.
        /// </summary>
        public ulong MessageContext
        {
            get;
            private set;
        }
        /// <summary>
        /// Create the EventArgs for when a message on a secure channel is received.
        /// </summary>
        /// <param name="messagecontext">The message context of the received message.</param>
        /// <param name="attributes">The attributes in the received message.</param>
        public SecureChannelMessageReceivedEventArgs(ulong messagecontext,
            Dictionary<string, byte[]> attributes)
        {
            Attributes = attributes;
            MessageContext = messagecontext;
        }
    }
}
