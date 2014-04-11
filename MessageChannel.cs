using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace Channels
{
    /// <summary>
    /// The class used for sending messages over the network.
    /// </summary>
    public class MessageChannel : IDisposable
    {
        /// <summary>
        /// The internal struct which contains the info for sending a message.
        /// </summary>
        private struct __MessageSendInfo
        {
            public ulong MessageContext;
            public byte MessageStartByte;
        }

        /// <summary>
        /// The is the underlying container for the message.
        /// </summary>
        private struct __Message
        {
            /// <summary>
            /// The byte at the very start of the message.
            /// </summary>
            public byte             MessageStartByte;
            /// <summary>
            /// The length of the message.
            /// </summary>
            public int              MessageLength;
            /// <summary>
            /// The identification number for the message.
            /// </summary>
            public ulong            MessageContext;

            /// <summary>
            /// The the message context that this message is in response to.
            /// </summary>
            public ulong            ResponseMessageContext;
            /// <summary>
            /// The the type id for the message.
            /// </summary>
            public ulong            MessageTypeId;
            /// <summary>
            /// The actual content of the message in bytes.
            /// </summary>
            public byte[]           MessageContentBuffer;
        }
        /// <summary>
        /// The message context that represents no / unknown message context.
        /// </summary>
        public const ulong UNKNOWN_CONTEXT = ulong.MaxValue;
        /// <summary>
        /// Returns the remote socket address for this channel.
        /// </summary>
        public SocketAddress RemoteAddress
        {
            get
            {
                return _ChannelSocket.RemoteEndPoint.Serialize();
            }
        }
        /// <summary>
        /// This event is triggered when there is an error with the channel.
        /// </summary>
        public event EventHandler<ChannelErrorEventArgs> Error;
        /// <summary>
        /// This event is triggered when a message is sent.
        /// </summary>
        public event EventHandler<ChannelMessageSendCompleteEventArgs> MessageSendComplete;
        /// <summar>ygm
        /// This event is triggered when a message is received.
        /// </summary>
        public event EventHandler<ChannelMessageReceivedEventArgs> MessageReceived;
        /// <summary>
        /// This event is triggered when the channel is disconnected.
        /// </summary>
        public event EventHandler<ChannelDisconnectedEventArgs> Disconnected;

        /// <summary>
        /// This should be called when a disconnection has occured.
        /// </summary>
        /// <returns>A bool indicating whether the channel has not been marked as disconnected yet or not.</returns>
        /// <param name="e">The event args to pass through to the event.</param>
        protected bool OnDisconnectionDetected(ChannelDisconnectedEventArgs e)
        {
            EventHandler<ChannelDisconnectedEventArgs> disconnected = Disconnected;
            _DisconnectedLock.EnterWriteLock();
            if (!_Disconnected)
            {
                _Disconnected = true;
                if (disconnected != null)
                {
                    disconnected(this, e);
                }
                _DisconnectedLock.ExitWriteLock();
                return true;
            }
            else
            {
                _DisconnectedLock.ExitWriteLock();
                return false;
            }
        }
        /// <summary>
        /// This should be called when an error has occurred.
        /// </summary>
        /// <param name="e">The event args to pass through to the event.</param>
        protected void OnError(ChannelErrorEventArgs e)
        {
            EventHandler<ChannelErrorEventArgs> error = Error;
            if (error != null)
            {
                error(this, e);
            }
        }
        /// <summary>
        /// This should be called when a message is received.
        /// </summary>
        /// <param name="e">The event args to pass through to the event.</param>
        protected void OnMessageReceived(ChannelMessageReceivedEventArgs e)
        {
            if (MessageReceived != null)
            {
                MessageReceived(this, e);
            }
        }
        /// <summary>
        /// This should be called when sending a message has completed sending.
        /// </summary>
        /// <param name="e">The event args to pass through to the event.</param>
        protected void OnMessageSendComplete(ChannelMessageSendCompleteEventArgs e)
        {
            EventHandler<ChannelMessageSendCompleteEventArgs> complete = MessageSendComplete;
            if (complete != null)
            {
                complete(this, e);
            }
        }
        /// <summary>
        /// This is an object used for locking the creation of message contexts.
        /// So that no two messages published by this channel have the same context.
        /// </summary>
        private object _MessageContextLock = new object();
        /// <summary>
        /// This contains the last issued message context.
        /// </summary>
        private ulong _CurrentMessageContext = 0;
        /// <summary>
        /// This creates the next message context to be used
        /// when messages are sent.
        /// </summary>
        /// <returns></returns>
        private ulong _CreateMessageContext()
        {
            lock (_MessageContextLock)
            {
                _CurrentMessageContext++;
                return _CurrentMessageContext;
            }
        }
        /// <summary>
        /// This is a lock around the _Disconnected flag so that it can
        /// be accessed from different threads. It is done via OnDisconnectionDetected
        /// </summary>
        private ReaderWriterLockSlim _DisconnectedLock;
        /// <summary>
        /// This indicates whether the channel is disconnected already or not.
        /// </summary>
        private bool _Disconnected;
        /// <summary>
        /// This is the stream that is used for publishing to the channel.
        /// It may be the compression the encryption stream
        /// or the network stream depending on what the handshake negotiated.
        /// </summary>
        private Stream              _ChannelOutputStream;
        /// <summary>
        /// This is the stream that is used to reading messages published to the channel.
        /// It may be the decompression stream or the decryption stream
        /// or the network stream depending on what the handshake negotiated.
        /// </summary>
        private Stream              _ChannelInputStream;
        /// <summary>
        /// The underlying network stream used for this channel.
        /// </summary>
        private NetworkStream       _ChannelNetworkStream;
        /// <summary>
        /// The socket used by this channel for communication.
        /// </summary>
        private Socket              _ChannelSocket;

        /// <summary>
        /// This is the auto reset event which can be used to trigger the receiving of messages.
        /// </summary>
        private AutoResetEvent      _ReceiveMessageAutoResetEvent;
        /// <summary>
        /// The thread used for receiving messages
        /// </summary>
        private Thread              _ReceiveMessagesThread;
        /// <summary>
        /// This is used to build up buffers until they are the correct length to be used for decoding.
        /// </summary>
        private NetworkBufferBuilder _BufferBuilder;


        /// <summary>
        /// These are the various constants used to identify information within a message.
        /// </summary>
        private const int           _MESSAGE_LENGTH_INDEX = 1;
        private const int           _MESSAGE_CONTEXT_INDEX = 1 + sizeof(int);
        private const int           _RESPONSE_MESSAGE_CONTEXT_INDEX = 1 + sizeof(int) + sizeof(ulong);
        private const int           _MESSAGE_TYPE_INDEX = 1 + sizeof(int) + 2 * sizeof(ulong);
        /// <summary>
        /// This is the byte that all messages must begin with.
        /// This can be changed so as to break compatability on a very low level.
        /// </summary>
        private const byte          _MESSAGE_START_BYTE = 71;
        private const byte          _MESSAGE_START_BYTE_INDEX = 0;
        private const byte          _HANDSHAKE_START_BYTE = 65;
        private const int           _MESSAGE_HEADER_LENGTH = 1 + sizeof(int) + 3 * sizeof(ulong);


        /// <summary>
        /// Creates a MessageChannel which can be used to transfer information
        /// over the network.
        /// </summary>
        /// <param name="socket">The socket used for communication.</param>
        public MessageChannel(Socket socket)
        {
            Disposed = false;
            _DisconnectedLock                       = new ReaderWriterLockSlim();
            _Disconnected                           = false;
            _ChannelSocket                          = socket;
            _ChannelNetworkStream                   = new NetworkStream(socket);
            _BufferBuilder                          = new NetworkBufferBuilder(_ChannelNetworkStream);
            _BufferBuilder.Disconnected             += new EventHandler<EventArgs>(_BufferBuilderDisconnected);
            _ChannelInputStream                     = _ChannelNetworkStream;
            _ChannelOutputStream                    = _ChannelNetworkStream;
            _ReceiveMessageAutoResetEvent           = new AutoResetEvent(false);
            _ReceiveMessagesThread = new Thread(new ThreadStart(_ReceiveMessages));
            _ReceiveMessagesThread.Start();
        }

        /// <summary>
        /// The event handler associated with the buffer builder experiancing a disconnection.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _BufferBuilderDisconnected(object sender, EventArgs e)
        {
            OnDisconnectionDetected(new ChannelDisconnectedEventArgs());
        }

        /// <summary>
        /// Disposes the channel. It will release all resources associated with it.
        /// </summary>
        public void Dispose()
        {
            Disposed = true; 
            _ReceiveMessageAutoResetEvent.Set();
            _BufferBuilder.Dispose();
            _ChannelSocket.Close();
            _ChannelSocket.Dispose();
            _ChannelNetworkStream.Close();
            _ChannelNetworkStream.Dispose();
            _ChannelOutputStream.Dispose();
            _ChannelInputStream.Dispose();
        }

        /// <summary>
        /// Indicates whether the channel is disposed or not.
        /// </summary>
        public bool Disposed
        {
            get;
            private set;
        }

        /// <summary>
        /// This runs in a loop collecting messages as they come in.
        /// </summary>
        private void _ReceiveMessages()
        {
            while (!Disposed)
            {
                if (_ChannelSocket.Connected)
                {
                    byte[] header = new byte[1 + sizeof(int) + 3 * sizeof(ulong)];
                    try
                    {
                        _BufferBuilder.BeginRead(header, header.Length, new AsyncCallback(_ReceiveMessageHeader), header);
                        
                    }
                    catch (IOException)
                    {
                        OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed,
                                                          ChannelErrorReason.Disconnected,
                                                          message: "Failed to receive messages. Socket closed."));
                        OnDisconnectionDetected(new ChannelDisconnectedEventArgs());

                    }
                }
                else
                {
                    OnDisconnectionDetected(new ChannelDisconnectedEventArgs());
                }
                _ReceiveMessageAutoResetEvent.WaitOne();
            }
        }
        
        /// <summary>
        /// The async subrutine to receive the header of the message.
        /// </summary>
        /// <param name="result">The IAsyncResult associated with the operation.</param>
        private void _ReceiveMessageHeader(IAsyncResult result)
        {
            __Message message;
            _BufferBuilder.EndRead(result);
            try
            {
                byte[] buffer = (byte[])result.AsyncState;
                message.MessageStartByte = buffer[_MESSAGE_START_BYTE_INDEX];
                message.MessageLength = BitConverter.ToInt32(buffer, _MESSAGE_LENGTH_INDEX);
                message.MessageContext = BitConverter.ToUInt64(buffer, _MESSAGE_CONTEXT_INDEX);
                message.ResponseMessageContext = BitConverter.ToUInt64(buffer, _RESPONSE_MESSAGE_CONTEXT_INDEX);
                message.MessageTypeId = BitConverter.ToUInt64(buffer, _MESSAGE_TYPE_INDEX);
                message.MessageContentBuffer = new byte[message.MessageLength - _MESSAGE_HEADER_LENGTH];
                //Don't try and read "nothing"
                if (message.MessageContentBuffer.Length != 0)
                {
                    _BufferBuilder.BeginRead(message.MessageContentBuffer,
                                                  message.MessageContentBuffer.Length,
                                                  new AsyncCallback(_ReceiveMessageContent), message);
                }
                else
                {
                    OnMessageReceived(new ChannelMessageReceivedEventArgs(message.MessageContext, message.MessageTypeId,
                        message.ResponseMessageContext, new Dictionary<string, byte[]>()));
                }
            }
            catch (ArgumentException)
            {
                OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed, ChannelErrorReason.ProtocolError));
                _ReceiveMessageAutoResetEvent.Set();
            }

        }

        /// <summary>
        /// The async subrutine needed to collect the actual
        /// main message chunck.
        /// </summary>
        /// <param name="result">The IAsyncResult assocated with the async operation to collect the data.</param>
        private void _ReceiveMessageContent(IAsyncResult result)
        {
            __Message message = (__Message)result.AsyncState;
            _BufferBuilder.EndRead(result);
            try
            {
                try
                {
                    Dictionary<string, byte[]> attributes = new Dictionary<string, byte[]>();
                    int readindex = 0;
                    while (readindex < message.MessageContentBuffer.Length)
                    {
                        int namelength = BitConverter.ToInt32(message.MessageContentBuffer, readindex);
                        readindex += sizeof(int);
                        string name = ASCIIEncoding.ASCII.GetString(message.MessageContentBuffer, readindex, namelength);
                        readindex += namelength;
                        int bufferlength = BitConverter.ToInt32(message.MessageContentBuffer, readindex);
                        if (bufferlength >= 0)
                        {
                            readindex += sizeof(int);
                            byte[] buffer = new byte[bufferlength];
                            Array.Copy(message.MessageContentBuffer, readindex, buffer, 0, bufferlength);
                            readindex += bufferlength;
                            attributes.Add(name, buffer);
                        }
                        else
                        {
                            OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed,
                                                              ChannelErrorReason.ProtocolError,
                                                              message.MessageContext,
                                                             "A negative buffer length was received."));
                        }
                    }
                    if (message.MessageStartByte == _MESSAGE_START_BYTE)
                    {
                        OnMessageReceived(new ChannelMessageReceivedEventArgs(message.MessageContext, message.MessageTypeId, message.ResponseMessageContext, attributes));
                    }
                    else
                    {
                        OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed,
                                                          ChannelErrorReason.ProtocolError,
                                                          message.MessageContext,
                                                          "Invalid message start byte."));
                    }
                }
                catch (ArgumentException ex)
                {

                    OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed,
                                                      ChannelErrorReason.ProtocolError,
                                                      message.MessageContext,
                                                      "Message or attribute length did not match actual length."));
                }
            }
            catch (SocketException)
            {
                OnDisconnectionDetected(new ChannelDisconnectedEventArgs());
                OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageReceiveFailed,
                        ChannelErrorReason.Disconnected, message.MessageContext));
            }
            _ReceiveMessageAutoResetEvent.Set();
        }

        /// <summary>
        /// Sends a message down the channel.
        /// </summary>
        /// <param name="messagetypecode">The type code for the message.</param>
        /// <param name="attributes">The dictionary of attributes that the message contains.</param>
        /// <param name="responsecontext">The message that this message is in response to.</param>
        /// <returns>The message context that the message will have.</returns>
        public ulong SendMessage(ulong messagetypecode,
                                          Dictionary<string, byte[]> attributes,
                                          ulong responsecontext = UNKNOWN_CONTEXT)
        {
            return _SendMessage(_MESSAGE_START_BYTE, messagetypecode, attributes, responsecontext);
        }

        /// <summary>
        /// The underlying subrutine to send a message.
        /// </summary>
        /// <param name="startbyte">The start byte to use for the message.</param>
        /// <param name="messagetypecode">The typecode of the message.</param>
        /// <param name="attributes">The dictionary of attributes that this message contains.</param>
        /// <param name="responsecontext">The message context that this message is in reply to.</param>
        /// <returns>The message context that the message will have.</returns>
        private ulong _SendMessage(byte startbyte,
                                   ulong messagetypecode,
                                   Dictionary<string, byte[]> attributes,
                                   ulong responsecontext = UNKNOWN_CONTEXT)
        {
            try
            {
                if (_ChannelSocket.Connected)
                {
                    __MessageSendInfo sendinfo;
                    sendinfo.MessageContext = _CreateMessageContext();
                    byte[] buffer = new byte[_GetMessageLength(attributes)];
                    buffer[0] = startbyte;
                    byte[] bytes = BitConverter.GetBytes(buffer.Length);
                    Array.Copy(bytes, 0, buffer, _MESSAGE_LENGTH_INDEX, bytes.Length);
                    bytes = BitConverter.GetBytes(sendinfo.MessageContext);
                    Array.Copy(bytes, 0, buffer, _MESSAGE_CONTEXT_INDEX, bytes.Length);
                    bytes = BitConverter.GetBytes(responsecontext);
                    Array.Copy(bytes, 0, buffer, _RESPONSE_MESSAGE_CONTEXT_INDEX, bytes.Length);
                    bytes = BitConverter.GetBytes(messagetypecode);
                    Array.Copy(bytes, 0, buffer, _MESSAGE_TYPE_INDEX, bytes.Length);
                    int index = _MESSAGE_TYPE_INDEX + sizeof(ulong);
                    foreach (KeyValuePair<string, byte[]> attr in attributes)
                    {
                        if (attr.Value.LongLength < int.MaxValue)
                        {
                            bytes = BitConverter.GetBytes(attr.Key.Length);
                            Array.Copy(bytes, 0, buffer, index, bytes.Length);
                            index += sizeof(int);
                            bytes = Encoding.ASCII.GetBytes(attr.Key);
                            Array.Copy(bytes, 0, buffer, index, bytes.Length);
                            index += attr.Key.Length;
                            bytes = BitConverter.GetBytes(attr.Value.Length);
                            Array.Copy(bytes, 0, buffer, index, bytes.Length);
                            index += sizeof(int);
                            Array.Copy(attr.Value, 0, buffer, index, attr.Value.Length);
                            index += attr.Value.Length;
                        }
                        else
                        {
                            OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageSendFailed,
                                                              ChannelErrorReason.MessageTooLong,
                                                              UNKNOWN_CONTEXT,
                                                              attr.Key + " is too large."));
                            return UNKNOWN_CONTEXT;
                        }
                    }
                    sendinfo.MessageStartByte = startbyte;
                    _ChannelOutputStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(_SendMessageCallback), sendinfo);
                    TcpClient tcpc = new TcpClient();
                    return sendinfo.MessageContext;
                }
                else
                {
                    OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageSendFailed,
                                                      ChannelErrorReason.Disconnected,
                                                      UNKNOWN_CONTEXT,
                                                      "Could not send message because the channel is not connected."));
                    OnDisconnectionDetected(new ChannelDisconnectedEventArgs());
                    return UNKNOWN_CONTEXT;
                }
            }
            catch (Exception ex)
            {
                OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageSendFailed));
                return UNKNOWN_CONTEXT;
            }
        }

        /// <summary>
        /// This is called when the message is sent.
        /// </summary>
        /// <param name="result">The IAsyncResult associated with sending the message.</param>
        private void _SendMessageCallback(IAsyncResult result)
        {
            __MessageSendInfo sendinfo = (__MessageSendInfo)result.AsyncState;
            try
            {
                _ChannelOutputStream.EndWrite(result);
                OnMessageSendComplete(new ChannelMessageSendCompleteEventArgs(sendinfo.MessageContext));

            }
            catch (IOException)
            {
                OnError(new ChannelErrorEventArgs(ChannelErrorType.MessageSendFailed, ChannelErrorReason.Disconnected, sendinfo.MessageContext));
            }
        }

        /// <summary>
        /// This is used to calculate the length of a message
        /// so that buffer sizes can be calculated.
        /// </summary>
        /// <param name="attributes">The attributes that will be sent.</param>
        /// <returns>The length of the message that would be compiled with the supplied attributes.</returns>
        private int _GetMessageLength(Dictionary<string, byte[]> attributes)
        {
            int length = _MESSAGE_HEADER_LENGTH; //Start byte, length, message context, response context, typecode
            foreach (KeyValuePair<string, byte[]> kvp in attributes)
            {
                length += kvp.Value.Length + kvp.Key.Length + 2 * sizeof(int); //+ the length of the the keysize + the buffer size
            }
            return length;
        }
    }
}
