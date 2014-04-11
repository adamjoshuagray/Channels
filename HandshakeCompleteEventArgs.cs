using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Channels
{
    /// <summary>
    /// The EventArgs for when a secure handshake has completed.
    /// </summary>
    public class SecureHandshakeCompleteEventArgs : EventArgs
    {
        /// <summary>
        /// The SecureChannel which has been created.
        /// </summary>
        public SecureChannel SecuredChannel
        {
            get;
            private set;
        }
        /// <summary>
        /// Creates an EventArgs for when a secure handshake has completed.
        /// </summary>
        /// <param name="schannel">The created SecureChannel.</param>
        public SecureHandshakeCompleteEventArgs(SecureChannel schannel)
        {
            SecuredChannel = schannel;
        }
    }
}
