using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace Channels
{
    public class ChannelListener : IDisposable
    {
        /// <summary>
        /// The name of this class so it can be used
        /// in the object disposed exeptions.
        /// </summary>
        private const string _OBJECT_NAME = "ChannelListener";

        /// <summary>
        /// Indicates whether the object has been disposed.
        /// </summary>
        public bool Disposed
        {
            get;
            private set;
        }
        
        /// <summary>
        /// This will dispose the object.
        /// Specifically it will stop the listener if it is running.
        /// </summary>
        public void Dispose()
        {
            Disposed = true;
            _TcpListener.Stop();
        }
        /// <summary>
        /// The underlying socket listener
        /// </summary>
        TcpListener _TcpListener;
        /// <summary>
        /// The maximum number of requests than can be backlogged
        /// on the port.
        /// </summary>
        private const int _MAX_BACKLOG = 10;

        /// <summary>
        /// This even it called if a channel connects.
        /// </summary>
        public event EventHandler<ChannelListenerConnectedEventArgs> Connected;

        /// <summary>
        /// This creates a new ChannelListener on the specified port.
        /// </summary>
        /// <param name="port">The port to listen for new connections on.</param>
        public ChannelListener(int port)
        {
            Disposed = false;
            _TcpListener = new TcpListener(port);
        }
        /// <summary>
        /// This will start listening for connections.
        /// </summary>
        public void StartListening()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(_OBJECT_NAME);
            }
            _TcpListener.Start(_MAX_BACKLOG);
            _AcceptLoop();
        }
        /// <summary>
        /// This will stop it from listening for connections.
        /// </summary>
        public void StopListening()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(_OBJECT_NAME);
            }
            _TcpListener.Stop();
        }

        /// <summary>
        /// This will begin accepting new sockets. It is called each time we reach the end of an accept
        /// process.
        /// </summary>
        private void _AcceptLoop()
        {
            try
            {
                _TcpListener.BeginAcceptSocket(new AsyncCallback(_Accepted), null);
            }
            catch (SocketException sex)
            {
                //Error here. TODO:
            }
            catch (ObjectDisposedException)
            {
                //Not an actual error. Occurs when disposal is going on.
            }
        }
        
        /// <summary>
        /// This is called when a socket is accepted.
        /// </summary>
        /// <param name="result"></param>
        private void _Accepted(IAsyncResult result)
        {

            try
            {
                //This piece of log makes sure that if the StopListening method has been called
                //then the _AcceptLoop is not recalled.
                if (result.IsCompleted)
                {
                    Socket soc = _TcpListener.EndAcceptSocket(result);
                    MessageChannel mc = new MessageChannel(soc);
                    if (Connected != null)
                    {
                        Connected(this, new ChannelListenerConnectedEventArgs(mc));
                    }
                    _AcceptLoop();
                }
            }
            catch
            {
                if (!Disposed)
                {
                    _AcceptLoop();
                }
            }
        }
    }
}
