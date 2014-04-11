using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace Channels
{
    /// <summary>
    /// This is a secure channel . It is used to exchange messages in a secure manner.
    /// An AES crypto transform is used to encrypt and decrupt messages. The secure handshaker can be
    /// used to turn a MessageChannel into a SecureChannel. That will do an RSA key exchange.
    /// </summary>
    public class SecureChannel : IDisposable
    {
        /// <summary>
        /// This is what the object is called. It is what is used for object disposed exceptions.
        /// </summary>
        private const string _OBJECT_NAME = "SecureChannel";
        /// <summary>
        /// This is the attribute name used to contain the boxed up message.
        /// </summary>
        private const string _ATTRIBUTE_NAME = "M";
        /// <summary>
        /// The underlying typecode that all messages must have. (This is the 1000th prime)
        /// </summary>
        private const ulong _MESSAGE_TYPE_CODE = 7919;
        /// <summary>
        /// The size of integers to use for length blocks.
        /// </summary>
        private const int _LENGTH_SIZE = sizeof(int);
        /// <summary>
        /// This just tells how large the decryption blocks should be.
        /// This makes no difference to the protocol and can be different
        /// on either end!
        /// </summary>
        private const int _DECRYPT_SIZE = 1024;

        /// <summary>
        /// This event is called when a message is received.
        /// </summary>
        public event EventHandler<SecureChannelMessageReceivedEventArgs> MessageReceived;
        /// <summary>
        /// This is called when there is an error in the channel.
        /// </summary>
        public event EventHandler<SecureChannelErrorEventArgs> Errored;

        /// <summary>
        /// This is called when the underlying channel is disconnected.
        /// </summary>
        public event EventHandler<ChannelDisconnectedEventArgs> Disconnected;

        /// <summary>
        /// The transform that we use to decrypt inbound messages.
        /// </summary>
        private ICryptoTransform _InboundCrypto;
        /// <summary>
        /// The transofmr that we use to encrypt outbound messages.
        /// </summary>
        private ICryptoTransform _OutboundCrypto;
        /// <summary>
        /// The underlying channel that messages are sent down.
        /// </summary>
        private MessageChannel _MessageChannel;
        /// <summary>
        /// Creates a secure channel which can be used to exchange messages
        /// securly.
        /// </summary>
        /// <param name="channel">The channel used to send the encrypted messages down.</param>
        /// <param name="outboundiv">The outbound IV to use with the AES</param>
        /// <param name="outboundkey">The key to use for outbound encryption.</param>
        /// <param name="inboundiv">The IV to use for inbound decryption.</param>
        /// <param name="inboundkey">The key to use for inbound decryption.</param>
        public SecureChannel(MessageChannel channel, 
                             byte[] outboundiv, byte[] outboundkey,
                             byte[] inboundiv, byte[] inboundkey)
        {
            Disposed = false;
            _MessageChannel = channel;
            _MessageChannel.Disconnected += new EventHandler<ChannelDisconnectedEventArgs>(_MessageChannel_Disconnected);
            
            _MessageChannel.Error += new EventHandler<ChannelErrorEventArgs>(_MessageChannel_Error);
            
            channel.MessageReceived += new EventHandler<ChannelMessageReceivedEventArgs>(channel_MessageReceived);
            AesManaged crypto = new AesManaged();
            crypto.Padding = PaddingMode.ISO10126;
            _OutboundCrypto = crypto.CreateEncryptor(outboundkey, outboundiv);
            _InboundCrypto = crypto.CreateDecryptor(inboundkey, inboundiv);
        }

        /// <summary>
        /// This is used to dispose this object.
        /// </summary>
        public void Dispose()
        {
            Disposed = true;
            _MessageChannel.Disconnected -= new EventHandler<ChannelDisconnectedEventArgs>(_MessageChannel_Disconnected);
            _MessageChannel.Error -= new EventHandler<ChannelErrorEventArgs>(_MessageChannel_Error);
            _MessageChannel.MessageReceived -= new EventHandler<ChannelMessageReceivedEventArgs>(channel_MessageReceived);
            _MessageChannel.Dispose();
        }

        /// <summary>
        /// Indicates whether the object has been disposed.
        /// </summary>
        public bool Disposed
        {
            get;
            private set;
        }
        /// <summary>
        /// Callback used for when the channel is disconnected.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _MessageChannel_Disconnected(object sender, ChannelDisconnectedEventArgs e)
        {
            if (Disconnected != null)
            {
                Disconnected(this, new ChannelDisconnectedEventArgs());
            }
        }

        /// <summary>
        /// Callback used for when the underlying channel has had an error.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _MessageChannel_Error(object sender, ChannelErrorEventArgs e)
        {
            if (Errored != null)
            {
                Errored(this, new SecureChannelErrorEventArgs(e.Type, e.Reason,
                    SecureChannelErrorType.Unknown, ""));
            }
        }

        /// <summary>
        /// Callback used for when the underlying channel has had a message received.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void channel_MessageReceived(object sender, ChannelMessageReceivedEventArgs e)
        {
            if (_ValidateMessage(e.MessageTypeCode, e.Attributes))
            {
                bool success = false;
                byte[] decbuf = _TryDecryptBuffer(e.Attributes[_ATTRIBUTE_NAME], out success);
                if (success)
                {
                    Dictionary<string, byte[]> attributes = _TryParseBuffer(decbuf, out success);
                    string asd = Encoding.ASCII.GetString(decbuf);
                    if (success)
                    {
                        if (MessageReceived != null)
                        {
                            MessageReceived(this, new SecureChannelMessageReceivedEventArgs(e.MessageContext, attributes));
                        }
                    }
                    else
                    {
                        if (Errored != null)
                        {
                            Errored(this, new SecureChannelErrorEventArgs(ChannelErrorType.Unknown,
                                ChannelErrorReason.Unknown, SecureChannelErrorType.FormatError, "Could not parse message."));
                        }
                    }
                }
                else
                {
                    if (Errored != null)
                    {
                        Errored(this, new SecureChannelErrorEventArgs(ChannelErrorType.Unknown,
                            ChannelErrorReason.Unknown, SecureChannelErrorType.CryptographyError, "Could not decrypt message."));
                    }
                }
            }
            else
            {
                Errored(this, new SecureChannelErrorEventArgs(ChannelErrorType.Unknown,
                    ChannelErrorReason.Unknown, SecureChannelErrorType.FormatError, "Could not parse message."));
            }
        }

        /// <summary>
        /// This will try and decrypt the buffer.
        /// </summary>
        /// <param name="buffer">The buffer that is to be decrypted and parsed.</param>
        /// <param name="success">A out paramter which indicates whether the operation was successful.</param>
        /// <returns>The dictionary of attributes that were derypted. (Null if success==false)</returns>
        private Dictionary<string, byte[]> _TryParseBuffer(byte[] buffer, out bool success)
        {
            int currentindex = 0;
            Dictionary<string, byte[]> attributes = new Dictionary<string, byte[]>();
            while (true)
            {
                int keylen = 0;
                if (buffer.Length >= currentindex + _LENGTH_SIZE)
                {
                    keylen = BitConverter.ToInt32(buffer, currentindex);
                }
                else
                {
                    success = false;
                    return null;
                }
                currentindex += _LENGTH_SIZE;
                string key = "";
                if (buffer.Length >= currentindex + keylen)
                {
                    key = ASCIIEncoding.ASCII.GetString(buffer, currentindex, keylen);
                }
                else
                {
                    success = false;
                    return null;
                }
                currentindex += keylen;
                int vallen = 0;
                if (buffer.Length >= currentindex + _LENGTH_SIZE)
                {
                    vallen = BitConverter.ToInt32(buffer, currentindex);
                }
                else
                {
                    success = false;
                    return null;
                }
                currentindex += _LENGTH_SIZE;
                byte[] valbuf = new byte[vallen];
                if (buffer.Length >= currentindex + vallen)
                {
                    Array.Copy(buffer, currentindex, valbuf, 0, vallen);
                }
                else
                {
                    success = false;
                    return null;
                }
                currentindex += vallen;
                if (attributes.ContainsKey(key))
                {
                    success = false;
                    return null;
                }
                else
                {
                    attributes.Add(key, valbuf);
                }
                if (currentindex == buffer.Length)
                {
                    success = true;
                    return attributes;
                }
            }
        }

        /// <summary>
        /// This will try make sure the message received has the correct paramters.
        /// </summary>
        /// <param name="messagetypecode">The typecode that the message has.</param>
        /// <param name="attributes">The attributes that the message has.</param>
        /// <returns>A bool indicating whether the message received is valid.</returns>
        private bool _ValidateMessage(ulong messagetypecode, Dictionary<string, byte[]> attributes)
        {
            return (messagetypecode == _MESSAGE_TYPE_CODE &&
                attributes.Count == 1 &&
                attributes.ContainsKey(_ATTRIBUTE_NAME));
        }

        /// <summary>
        /// This will try and decrypt to received message.
        /// </summary>
        /// <param name="buffer">The buffer that is to be decrypted.</param>
        /// <param name="success">A bool indicating whether the decyption was successful.</param>
        /// <returns>A buffer which has been decrypted. (Null if success==false)</returns>
        private byte[] _TryDecryptBuffer(byte[] buffer, out bool success)
        {
            try
            {
                MemoryStream ims = new MemoryStream(buffer, false);
                MemoryStream oms = new MemoryStream();
                CryptoStream cs = new CryptoStream(ims, _InboundCrypto, CryptoStreamMode.Read);
                byte[] tbuf = new byte[_DECRYPT_SIZE];
                int read = cs.Read(tbuf, 0, _DECRYPT_SIZE);
                while (read > 0)
                {
                    oms.Write(tbuf, 0, read);
                    read = cs.Read(tbuf, 0, _DECRYPT_SIZE);
                }
                success = true;
                return oms.ToArray();
            }
            catch
            {
                success = false;
                return null;
            }
            
        }
        /// <summary>
        /// This will send a message down the secure channel.
        /// </summary>
        /// <param name="attributes">The attributes.</param>
        /// <exception cref="ObjectDisposedException"/>
        /// <returns>The message context that the message was sent has. Note that this number is not encrypted.</returns>
        public ulong SendMessage(Dictionary<string, byte[]> attributes)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(_OBJECT_NAME);
            }
            Dictionary<string, byte[]> crypted = new Dictionary<string,byte[]>();
            crypted.Add(_ATTRIBUTE_NAME, _EncryptBuffer(_CompileBuffer(attributes)));
            return _MessageChannel.SendMessage(_MESSAGE_TYPE_CODE, crypted);
        }

        /// <summary>
        /// This will compile a buffer given a set of attributes.
        /// </summary>
        /// <param name="attributes">The attributes to be packaged up into a buffer.</param>
        /// <returns>A buffer which is a serialised version of the attributes.</returns>
        private byte[] _CompileBuffer(Dictionary<string, byte[]> attributes)
        {
            byte[] buffer = new byte[_CalculateBufferLength(attributes)];
            int currentindex = 0;
            foreach (KeyValuePair<string, byte[]> kvp in attributes)
            {
                byte[] buf = BitConverter.GetBytes(kvp.Key.Length);
                Array.Copy(buf, 0, buffer, currentindex, _LENGTH_SIZE);
                currentindex += _LENGTH_SIZE;
                buf = ASCIIEncoding.ASCII.GetBytes(kvp.Key);
                Array.Copy(buf, 0, buffer, currentindex, buf.Length);
                buf = BitConverter.GetBytes(kvp.Value.Length);
                currentindex += kvp.Key.Length;
                Array.Copy(buf, 0, buffer, currentindex, _LENGTH_SIZE);
                currentindex += _LENGTH_SIZE;
                Array.Copy(kvp.Value, 0, buffer, currentindex, kvp.Value.Length);
                currentindex += kvp.Value.Length;
            }
            return buffer;
        }
        /// <summary>
        /// This will take a buffer and encrypt it.
        /// </summary>
        /// <param name="buffer">The buffer which is to be encrypted.</param>
        /// <returns>The encrypted buffer.</returns>
        private byte[] _EncryptBuffer(byte[] buffer)
        {
            MemoryStream ms = new MemoryStream();
            CryptoStream cs = new CryptoStream(ms, _OutboundCrypto, CryptoStreamMode.Write);
            cs.Write(buffer, 0, buffer.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }

        /// <summary>
        /// This will calculate the buffer length.
        /// </summary>
        /// <param name="attributes">The attributes which are to be serialised into the buffer.</param>
        /// <returns>The length of the buffer that will be needed.</returns>
        private int _CalculateBufferLength(Dictionary<string, byte[]> attributes)
        {
            int length = 0;
            foreach (KeyValuePair<string, byte[]> kvp in attributes)
            {
                length += 2 * _LENGTH_SIZE;
                length += kvp.Key.Length;
                length += kvp.Value.Length;
            }
            return length;
        }
    }
}
