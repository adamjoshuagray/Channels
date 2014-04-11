using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Channels
{
    /// <summary>
    /// The types of errors that can occur with a MessageChannel
    /// </summary>
    public enum ChannelErrorType
    {
        Unknown                 =           0,
        MessageSendFailed       =           1,
        MessageTooLong          =           2,
        MessageReceiveFailed    =           3
    }


    /// <summary>
    /// A reason an error occured.
    /// </summary>
    public enum ChannelErrorReason
    {
        Unknown                 =           0,
        Disconnected            =           1,
        ProtocolError           =           2,
        MessageTooLong          =           3,
        ChannelNotReady         =           4
    }

    /// <summary>
    /// The event arguments which describe a message.
    /// </summary>
    public class ChannelMessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The message type id.
        /// This is used to denote the type of message that was sent.
        /// It can be used in anyway by a higher level implementation.
        /// </summary>
        public ulong MessageTypeCode
        {
            get;
            private set;
        }

        /// <summary>
        /// This is the message context of the message. It uniquly identifies
        /// messages sent from the remote node to this node.
        /// </summary>
        public ulong MessageContext
        {
            get;
            private set;
        }
        /// <summary>
        /// This proprty indicates which message sent by the local node, that this message
        /// is in direct response to. If this message is not in direct response to any messages
        /// sent by the local node then this property is set to MessageChannel.UNKNOWN_CONTEXT
        /// </summary>
        public ulong ResponseMessageContext
        {
            get;
            private set;
        }
        /// <summary>
        /// This is the set of attributes available with this message.
        /// These contain the keys and values that are sent in this message.
        /// </summary>
        public Dictionary<string, byte[]> Attributes
        {
            get;
            private set;
        }

        /// <summary>
        /// This will return a string representation of all the attributes in this message.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string> GetAttributesAsStrings()
        {
            Dictionary<string, string> attrs = new Dictionary<string, string>();
            foreach (KeyValuePair<string, byte[]> kvp in Attributes)
            {
                attrs.Add(kvp.Key, Encoding.ASCII.GetString(kvp.Value));
            }
            return attrs;
        }

        /// <summary>
        /// Creates a message received event arguments.
        /// </summary>
        /// <param name="messagecontext">The context of the message.</param>
        /// <param name="messagetypeid">The message typ id for the message.</param>
        /// <param name="responsemessagecontext">The response context for th message.</param>
        /// <param name="attributes">The attributes for the message.</param>
        public ChannelMessageReceivedEventArgs(ulong messagecontext,
                                        ulong messagetypeid,
                                        ulong responsemessagecontext = MessageChannel.UNKNOWN_CONTEXT,
                                        Dictionary<string, byte[]> attributes = null)
        {
            MessageContext              =       messagecontext;
            ResponseMessageContext      =       responsemessagecontext;
            MessageTypeCode             =       messagetypeid;
            Attributes                  =       attributes;
        }
    }

    /// <summary>
    /// Event args for when a channel becomes disconnected.
    /// </summary>
    public class ChannelDisconnectedEventArgs : EventArgs
    {
    }

    /// <summary>
    /// EventArgs for then a channel has an error.
    /// </summary>
    public class ChannelErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The MessageContext (if available) of the error.
        /// </summary>
        public ulong MessageContext
        {
            get;
            private set;
        }

        /// <summary>
        /// The type of error that occured.
        /// </summary>
        public ChannelErrorType Type
        {
            get;
            private set;
        }

        /// <summary>
        /// The associated message about the error.
        /// </summary>
        public string Message
        {
            get;
            private set;
        }

        /// <summary>
        /// The reason the error occured.
        /// </summary>
        public ChannelErrorReason Reason
        {
            get;
            private set;
        }

        /// <summary>
        /// Creates a new error event args object.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="reason"></param>
        /// <param name="messagecontext"></param>
        /// <param name="message"></param>
        public ChannelErrorEventArgs(ChannelErrorType type, ChannelErrorReason reason = ChannelErrorReason.Unknown, ulong messagecontext = MessageChannel.UNKNOWN_CONTEXT, string message = "")
        {
            Type                    =           type;
            MessageContext          =           messagecontext;
            Message                 =           message;
            Reason                  =           reason;
        }
    }

    /// <summary>
    /// EventArgs for when a message's sending has completed.
    /// </summary>
    public class ChannelMessageSendCompleteEventArgs : EventArgs
    {
        /// <summary>
        /// The message context of the message which completed sending.
        /// </summary>
        public ulong MessageContext
        {
            get;
            private set;
        }
        /// <summary>
        /// Creates a new EventArgs for the when a message has completed sending.
        /// </summary>
        /// <param name="messagecontext"></param>
        public ChannelMessageSendCompleteEventArgs(ulong messagecontext)
        {
            MessageContext = messagecontext;
        }
    }
}
