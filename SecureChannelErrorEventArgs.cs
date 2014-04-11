using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Channels
{

    /// <summary>
    /// The enum which indicates the type of error
    /// </summary>
    public enum SecureChannelErrorType
    {
        Unknown = 0,
        ChannelDisconnected = 1,
        FormatError = 2,
        CryptographyError = 3
    }

    /// <summary>
    /// The EventArgs for when the secure channel has an error.
    /// </summary>
    public class SecureChannelErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The reason (if any) that the underlying channel has had an error.
        /// </summary>
        public ChannelErrorReason ChannelErrorReason
        {
            get;
            private set;
        }
        /// <summary>
        /// The type (if any) of the error that the underlying channel has had an error.
        /// </summary>
        public ChannelErrorType ChannelErrorType
        {
            get;
            private set;
        }
        /// <summary>
        /// The type of error that the secure channel has had.
        /// </summary>
        public SecureChannelErrorType SecureErrorType
        {
            get;
            private set;
        }
        /// <summary>
        /// A string describing the reason that the secure channel has had an error.
        /// </summary>
        public string SecureErrorReason
        {
            get;
            private set;
        }
        /// <summary>
        /// Creates the EventArgs for when the secure channel has had an error.
        /// </summary>
        /// <param name="cerrortype">The error type from the underlying channel.</param>
        /// <param name="cerrorreason">The reason behind the underlying channel's error.</param>
        /// <param name="scerrortype">The type of secure channel error.</param>
        /// <param name="scerrorreason">The string describing the reason behind the secure channel's error.</param>
        public SecureChannelErrorEventArgs(ChannelErrorType cerrortype, ChannelErrorReason cerrorreason,
            SecureChannelErrorType scerrortype, string scerrorreason)
        {
            ChannelErrorReason = cerrorreason;
            ChannelErrorType = cerrortype;
            SecureErrorType = scerrortype;
            SecureErrorReason = scerrorreason;
        }
    }
}
