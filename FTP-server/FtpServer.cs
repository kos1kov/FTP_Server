using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FTP_server
{
    class FtpServer: IDisposable
    {
        public FtpServer()
        {

        }

        private bool _listening = false;
        private TcpListener _listener;
        public void Start()
        {
            _listening = true;
            _listener = new TcpListener(IPAddress.Any, 21);
            _listener.Start();
            _activeConnections = new List<ClientConnection>();
            _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
        }

        public void Stop()
        {
            _listening = false;
            _listener.Stop();
            _listener = null;
            
        }

        private void HandleAcceptTcpClient(IAsyncResult result)
        {
            
                _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
                TcpClient client = _listener.EndAcceptTcpClient(result);

                ClientConnection connection = new ClientConnection(client);

                ThreadPool.QueueUserWorkItem(connection.HandleClient, client);
            
        }

        #region IDisposable Support
        private bool disposedValue = false; // Для определения избыточных вызовов
        private List<ClientConnection> _activeConnections;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                }
                disposedValue = true;
            }
        }


        
        public void Dispose()
        {
         
            Dispose(true);
        
        }
        #endregion
    }
}

