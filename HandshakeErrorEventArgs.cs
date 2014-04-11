using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Channels
{

    /// <summary>
    /// The type of error that occured when performing a secure handshake.
    /// </summary>
    public enum SecureHandshakingErrorType
    {
        Unknown = 0,
        ChannelError = 1,
        ChannelDisconnected = 2,
        RSADecryptionFailed = 3,
        FormatError = 4
    }

    /// <summary>
    /// The EventArgs which are used when a secure handshake errors.
    /// </summary>
    public class SecureHandshakeErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The channel which was used for the handshaking.
        /// </summary>
        public MessageChannel Channel
        {
            get;
            private set;
        }

        /// <summary>
        /// A string describing why the handshake errored.
        /// </summary>
        public string HandshakeErrorReason
        {
            get;
            private set;
        }
        /// <summary>
        /// If there was an underlying channel error
        /// the reason the channel errored.
        /// </summary>
        public ChannelErrorReason UnderlyingChannelReason
        {
            get;
            private set;
        }

        /// <summary>
        /// The type of handshaking errored that occured.
        /// </summary>
        public SecureHandshakingErrorType HandshakeErrorType
        {
            get;
            private set;
        }

        /// <summary>
        /// If there was an underlying channel error the type of
        /// channel error that occured.
        /// </summary>
        public ChannelErrorType UnderlyingChannelErrorType
        {
            get;
            private set;
        }
        /// <summary>
        /// Create the EventArgs for when a secure channel handshake errors.
        /// </summary>
        /// <param name="hserror">The type of handshaking error.</param>
        /// <param name="hsreason">The string describing why the handshaking error occured.</param>
        /// <param name="cerror">The type of channel error.</param>
        /// <param name="creason">The reason behind the underlying channel error.</param>
        /// <param name="channel">The channel used for the handshake.</param>
        public SecureHandshakeErrorEventArgs(SecureHandshakingErrorType hserror, string hsreason, ChannelErrorType cerror, ChannelErrorReason creason, MessageChannel channel)
        {
            HandshakeErrorReason = hsreason;
            UnderlyingChannelReason = creason;
            HandshakeErrorType = hserror;
            UnderlyingChannelErrorType = cerror;
            Channel = channel;
        }
    }
}
