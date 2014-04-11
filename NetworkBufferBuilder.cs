using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Channels
{
    class NetworkBufferBuilder : IDisposable
    {
        /// <summary>
        /// The request to read that was received.
        /// This struct is used to deal with multiple read requests that
        /// may queue up while one read is still being handled.
        /// </summary>
        private struct _ReadRequest
        {
            /// <summary>
            /// The state object associated with the async event.
            /// </summary>
            public object State;
            /// <summary>
            /// The buffer to read the data into.
            /// </summary>
            public byte[] buffer;
            /// <summary>
            /// The callback which is fired when all the data has been read.
            /// </summary>
            public AsyncCallback Callback;
            /// <summary>
            /// The length of the data to be collected.
            /// </summary>
            public int Length;
            /// <summary>
            /// The position within the buffer that data has been filled up to.
            /// </summary>
            public int CurrentIndex;
        }
        /// <summary>
        /// This is used to deal with async results that must be created for
        /// client code. This simply implements IAsyncResult but the actual
        /// concrete type is not visible to clients.
        /// </summary>
        private class _ReadCompleteAsyncResult : IAsyncResult
        {
            /// <summary>
            /// Whether the async result is completed.
            /// For the purposes of this event if this object
            /// is created then it is complete and thus the
            /// property always returns true.
            /// </summary>
            public bool IsCompleted
            {
                get
                {
                    return true;
                }
            }
            /// <summary>
            /// This always returns false because it is the safest result to return.
            /// It is more or less impossible for this to complete synchronously.
            /// </summary>
            public bool CompletedSynchronously
            {
                get
                {
                    return false;
                }
            }
            /// <summary>
            /// The state variable for the event.
            /// </summary>
            public object AsyncState
            {
                get;
                private set;
            }
            /// <summary>
            /// This is set to the wait handle used by the networkstream.
            /// </summary>
            public WaitHandle AsyncWaitHandle
            {
                get;
                private set;
            }
            /// <summary>
            /// Creates IAsyncResult.
            /// </summary>
            /// <param name="state">The state variable.</param>
            /// <param name="handle">The network stream's wait handle.</param>
            public _ReadCompleteAsyncResult(object state, WaitHandle handle)
            {
                AsyncState = state;
                AsyncWaitHandle = handle;
            }
        }

        /// <summary>
        /// The current request being serviced.
        /// </summary>
        private _ReadRequest _CurrentRequest;
        /// <summary>
        /// A queue of read requests which need to be completed.
        /// </summary>
        private ConcurrentQueue<_ReadRequest> _ReadRequests;
        /// <summary>
        /// This is the wait event for getting read requests from the client.
        /// </summary>
        private AutoResetEvent _ReadRequestWaitEvent;
        /// <summary>
        /// This is the wait even used to announce the completion of reading for a particular event.
        /// </summary>
        private AutoResetEvent _ReadFinishedWaitEvent;
        /// <summary>
        /// This is used to synchronously dispose of the object.
        /// </summary>
        private AutoResetEvent _DisposeWaitEvent;

        /// <summary>
        /// The underlying network stream to use.
        /// </summary>
        private NetworkStream _NetworkStream;

        private Thread _Thread;

        /// <summary>
        /// This is called when the underlying network stream disconnects.
        /// </summary>
        public event EventHandler<EventArgs> Disconnected;

        /// <summary>
        /// Indicates whether the object has been disposed of yet.
        /// </summary>
        public bool Disposed
        {
            get;
            private set;
        }

        /// <summary>
        /// Creates the buffer builder.
        /// </summary>
        /// <param name="stream">The underlying network stream to use.</param>
        public NetworkBufferBuilder(NetworkStream stream)
        {
            _NetworkStream = stream;
            Disposed = false;
            _ReadRequestWaitEvent = new AutoResetEvent(false);
            _ReadFinishedWaitEvent = new AutoResetEvent(true);
            _DisposeWaitEvent = new AutoResetEvent(false);
            _ReadRequests = new ConcurrentQueue<_ReadRequest>();
            _Thread = new Thread(new ThreadStart(_ServiceReads));
            _Thread.Start();
        }

        /// <summary>
        /// This is used to start reading untill enough information is collected.
        /// </summary>
        /// <param name="buffer">The byte buffer to dump the data into.</param>
        /// <param name="length">The length of the buffer to collect.</param>
        /// <param name="callback">The callback to call once all the data is collected.</param>
        /// <param name="state">A state object to be passed through to the callback.</param>
        public void BeginRead(byte[] buffer, int length, AsyncCallback callback, object state)
        {
            _ReadRequest rr = new _ReadRequest();
            rr.buffer = buffer;
            rr.Length = length;
            rr.Callback = callback;
            rr.State = state;
            rr.CurrentIndex = 0;
            _ReadRequests.Enqueue(rr);
            _ReadRequestWaitEvent.Set();
        }

        /// <summary>
        /// The loop that is used to read the data.
        /// </summary>
        private void _ServiceReads()
        {
            while (!Disposed)
            {
                _ReadRequestWaitEvent.WaitOne();
                _ReadFinishedWaitEvent.WaitOne();
                if (_ReadRequests.TryDequeue(out _CurrentRequest))
                {
                    _ReadBytes();
                }
            }
            _DisposeWaitEvent.Set();
        }

        /// <summary>
        /// This is the subrutine for reading bytes.
        /// </summary>
        private void _ReadBytes()
        {
            try
            {
                _NetworkStream.BeginRead(_CurrentRequest.buffer,
                                         _CurrentRequest.CurrentIndex,
                                         _CurrentRequest.Length - _CurrentRequest.CurrentIndex,
                                         new AsyncCallback(_BytesRead),
                                         null);
            }
            catch (IOException)
            {
                if (Disconnected != null)
                {
                    Disconnected(this, new EventArgs());
                }
            }
        }

        /// <summary>
        /// The callback subrutine for after the bytes are read.
        /// </summary>
        /// <param name="result">The IAsyncResult for the callback.</param>
        private void _BytesRead(IAsyncResult result)
        {
            try
            {
                int size = _NetworkStream.EndRead(result);
                _CurrentRequest.CurrentIndex += size;
                if (size == 0)
                {
                    if (Disconnected != null)
                    {
                        Disconnected(this, new EventArgs());
                    }
                    return;
                }
                if (_CurrentRequest.CurrentIndex != _CurrentRequest.Length)
                {
                    _ReadBytes();
                }
                else
                {
                    _CurrentRequest.Callback(new _ReadCompleteAsyncResult(_CurrentRequest.State, result.AsyncWaitHandle));
                }
            }
            catch (IOException)
            {
                if (Disconnected != null)
                {
                    Disconnected(this, new EventArgs());
                }
            }
            catch (ObjectDisposedException)
            {
                if (Disconnected != null)
                {
                    Disconnected(this, new EventArgs());
                }
            }
        }

        /// <summary>
        /// This is used to finish reading. It should be called when the callback provided in BeginRead is called.
        /// </summary>
        /// <param name="result"></param>
        public void EndRead(IAsyncResult result)
        {
            _ReadFinishedWaitEvent.Set();
        }

        /// <summary>
        /// Used to dispose the object.
        /// </summary>
        public void Dispose()
        {
            Disposed = true;
            _NetworkStream.Dispose();
            _ReadRequestWaitEvent.Set();
            _ReadFinishedWaitEvent.Set();
            _DisposeWaitEvent.WaitOne();
        }


    }
}
