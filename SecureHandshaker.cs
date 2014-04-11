using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// A handshaker that takes a MessageChannel and performs a handshake to get a secure channel.
    /// </summary>
    public class SecureHandshaker : IDisposable
    {
        /// <summary>
        /// The underlying channel.
        /// </summary>
        private MessageChannel _Channel;

        //Bunch of constants which control how the handshaking works.

        private const string _OBJECT_NAME = "SecureHandshaker";
        private const ulong _HANDSHAKE_RSA_TYPE_CODE = 4391;
        private const ulong _HANDSHAKE_AES_TYPE_CODE = 4392;
        private const string _RSA_ATTRIBUTE_NAME = "R";
        private const string _AES_IV_ATTRIBUTE_NAME = "V";
        private const string _AES_KEY_ATTRIBUTE_NAME = "K";
        private const int _OUTBOUND_RSA_KEY_LENGTH = 3072;


        /// <summary>
        /// The RSA transform for inbound parts of the handshake.
        /// </summary>
        private RSACryptoServiceProvider _RSAInboundProvider;
        /// <summary>
        /// The RSA transform for the outbound parts of the handshake.
        /// </summary>
        private RSACryptoServiceProvider _RSAOutboundProvider;
        /// <summary>
        /// A bool indicating whether the outbound part has been completed.
        /// </summary>
        private bool _OutboundComplete;
        /// <summary>
        /// A bool indicating whther the inbound part has been completed.
        /// </summary>
        private bool _InboundComplete;
        /// <summary>
        /// This is used to sync up events so that a handshake even is called and after that returns
        /// the channel event is allowed to return.
        /// </summary>
        private AutoResetEvent _AreEventSync;
        /// <summary>
        /// The auto reset event for the handshake
        /// </summary>
        private AutoResetEvent _AreHandshake;
        /// <summary>
        /// The AES transform to use for outbound messages.
        /// </summary>
        private AesManaged _OutboundAES;
        /// <summary>
        /// The AES transofrm to use for inbound messages.
        /// </summary>
        private AesManaged _InboundAES;
        /// <summary>
        /// The thread that deals with the completion of the handshake.
        /// </summary>
        private Thread _CompletionThread;
        /// <summary>
        /// The event for when a handshake has been completed.
        /// </summary>
        public event EventHandler<SecureHandshakeCompleteEventArgs> HandshakeCompleted;
        /// <summary>
        /// The event for when a handshake has failed.
        /// </summary>
        public event EventHandler<SecureHandshakeErrorEventArgs> HandshakeErrored;

        /// <summary>
        /// This will dispose the object.
        /// </summary>
        public void Dispose()
        {
            Disposed = true;
            _AreHandshake.Set();
            _AreHandshake.Set();
            _AreEventSync.Set();
            _Channel.Error -= new EventHandler<ChannelErrorEventArgs>(_Channel_Error);
            _Channel.Disconnected -= new EventHandler<ChannelDisconnectedEventArgs>(_Channel_Disconnected);
            _Channel.MessageSendComplete -= new EventHandler<ChannelMessageSendCompleteEventArgs>(_Channel_MessageSendComplete);
            _Channel.MessageReceived -= new EventHandler<ChannelMessageReceivedEventArgs>(_Channel_MessageReceived);
        }

        /// <summary>
        /// This is called when a handshake error has occured. It mananges the calling of
        /// the relevent event and then disposing of this object.
        /// </summary>
        /// <param name="e">The event args to call the event with.</param>
        private void OnHandshakeError(SecureHandshakeErrorEventArgs e)
        {
            if (HandshakeErrored != null)
            {
                HandshakeErrored(this, e);
            }
            this.Dispose();
        }

        /// <summary>
        /// A bool indicating whether the object has been disposed yet or not.
        /// </summary>
        public bool Disposed
        {
            get;
            private set;
        }

        /// <summary>
        /// Creates a new secure handshaker which will try and turn a channel into a secure channel.
        /// </summary>
        /// <param name="channel">The underlying message channel to use.</param>
        public SecureHandshaker(MessageChannel channel)
        {
            _AreEventSync = new AutoResetEvent(false);
            _OutboundComplete = false;
            _InboundComplete = false;
            _AreHandshake = new AutoResetEvent(false);
            Disposed = false;
            _CompletionThread = new Thread(new ThreadStart(_WaitComplete));
            _CompletionThread.Start();
            _InboundAES = new AesManaged();
            _OutboundAES = new AesManaged();
            _OutboundAES.GenerateIV();
            _OutboundAES.GenerateKey();
            _Channel = channel;
            _Channel.Error += new EventHandler<ChannelErrorEventArgs>(_Channel_Error);
            _Channel.Disconnected += new EventHandler<ChannelDisconnectedEventArgs>(_Channel_Disconnected);
            _Channel.MessageSendComplete += new EventHandler<ChannelMessageSendCompleteEventArgs>(_Channel_MessageSendComplete);
            _Channel.MessageReceived += new EventHandler<ChannelMessageReceivedEventArgs>(_Channel_MessageReceived);
            _RSAInboundProvider = new RSACryptoServiceProvider(_OUTBOUND_RSA_KEY_LENGTH);
        }

        /// <summary>
        /// This is called when the underlying channel is disconnected.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _Channel_Disconnected(object sender, ChannelDisconnectedEventArgs e)
        {

            OnHandshakeError(new SecureHandshakeErrorEventArgs(SecureHandshakingErrorType.ChannelDisconnected, "Underlying channel disconnected", ChannelErrorType.Unknown, ChannelErrorReason.Disconnected, _Channel));           
        }
        /// <summary>
        /// This is called when the underlying channel has an error.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _Channel_Error(object sender, ChannelErrorEventArgs e)
        {
            OnHandshakeError(new SecureHandshakeErrorEventArgs(SecureHandshakingErrorType.ChannelError, "", e.Type, e.Reason, _Channel));
        }
        /// <summary>
        /// This is called when the underlying channel has finished sending a message.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _Channel_MessageSendComplete(object sender, ChannelMessageSendCompleteEventArgs e)
        {
            //Nothing to do ATM.
        }

        /// <summary>
        /// This waits for the parts of the handshake to complete then it will
        /// call the relevent events.
        /// </summary>
        private void _WaitComplete()
        {
            _AreHandshake.WaitOne();
            _AreEventSync.Set();
            _AreHandshake.WaitOne();
            //This makes sure they are complete and not infact errored.
            if (_InboundComplete && _OutboundComplete)
            {
                SecureChannel schannel = new SecureChannel(_Channel, _OutboundAES.IV,
                    _OutboundAES.Key, _InboundAES.IV, _InboundAES.Key);
                this.Dispose();
                if (HandshakeCompleted != null)
                {
                    HandshakeCompleted(this, new SecureHandshakeCompleteEventArgs(schannel));
                }
                _AreEventSync.Set();
            }
        }

        /// <summary>
        /// This is used to send the AES key (and IV) to the other end of the channel.
        /// </summary>
        private void _SendAESKey()
        {
            Dictionary<string, byte[]> attributes = new Dictionary<string, byte[]>();
            byte[] rsaenciv = _RSAOutboundProvider.Encrypt(_OutboundAES.IV, true);
            attributes.Add(_AES_IV_ATTRIBUTE_NAME, rsaenciv);
            byte[] rsaenckey = _RSAOutboundProvider.Encrypt(_OutboundAES.Key, true);
            attributes.Add(_AES_KEY_ATTRIBUTE_NAME, rsaenckey);
            _Channel.SendMessage(_HANDSHAKE_AES_TYPE_CODE, attributes);
        }

        /// <summary>
        /// This is called when a message from the underlying channel has been received.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _Channel_MessageReceived(object sender, ChannelMessageReceivedEventArgs e)
        {
            if (e.MessageTypeCode == _HANDSHAKE_RSA_TYPE_CODE)
            {
                if (e.Attributes.Count == 1 &&
                    e.Attributes.ContainsKey(_RSA_ATTRIBUTE_NAME))
                {
                    byte[] rsablob = e.Attributes[_RSA_ATTRIBUTE_NAME];
                    _RSAOutboundProvider = new RSACryptoServiceProvider();
                    _RSAOutboundProvider.ImportCspBlob(rsablob);
                    _SendAESKey();
                    _OutboundComplete = true;
                    _AreHandshake.Set();
                    _AreEventSync.WaitOne();
                }
                else
                {
                    OnHandshakeError(new SecureHandshakeErrorEventArgs(SecureHandshakingErrorType.FormatError, "Wrong attributes in RSA message.", ChannelErrorType.Unknown, ChannelErrorReason.Unknown, _Channel));
                }
            }
            else if (e.MessageTypeCode == _HANDSHAKE_AES_TYPE_CODE)
            {
                if (e.Attributes.Count == 2 &&
                    e.Attributes.ContainsKey(_AES_IV_ATTRIBUTE_NAME) &&
                    e.Attributes.ContainsKey(_AES_KEY_ATTRIBUTE_NAME))
                {
                    try
                    {
                        byte[] rsadeckey = _RSAInboundProvider.Decrypt(e.Attributes[_AES_KEY_ATTRIBUTE_NAME], true);
                        byte[] rsadeciv = _RSAInboundProvider.Decrypt(e.Attributes[_AES_IV_ATTRIBUTE_NAME], true);
                        _InboundAES.Key = rsadeckey;
                        _InboundAES.IV = rsadeciv;
                        _InboundComplete = true;
                        _AreHandshake.Set();
                        _AreEventSync.WaitOne();
                    }
                    catch (CryptographicException)
                    {
                        OnHandshakeError(new SecureHandshakeErrorEventArgs(SecureHandshakingErrorType.RSADecryptionFailed, "RSA decryption failed", ChannelErrorType.Unknown, ChannelErrorReason.Unknown, _Channel));
                    }
                }
                else
                {
                    OnHandshakeError(new SecureHandshakeErrorEventArgs(SecureHandshakingErrorType.FormatError, "Wrong attributes in AES message.", ChannelErrorType.Unknown, ChannelErrorReason.Unknown, _Channel));
                }
            }
        }

        /// <summary>
        /// This will start the sending part of the handshake.
        /// </summary>
        public void SendHandshake()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(_OBJECT_NAME);
            }
            Dictionary<string, byte[]> attributes = new Dictionary<string, byte[]>();
            //Very important that this is set to false or the key exchange is not secure!
            attributes.Add(_RSA_ATTRIBUTE_NAME, _RSAInboundProvider.ExportCspBlob(false));
            _Channel.SendMessage(_HANDSHAKE_RSA_TYPE_CODE, attributes);
        }

    }
}
