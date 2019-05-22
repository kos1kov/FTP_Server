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
      public static  ApplicationContext db = new ApplicationContext();
        public FtpServer()
        {
            db.Database.EnsureCreated();
        }

        private TcpListener _listener;
        public async Task Start()
        {

            _listener = new TcpListener(IPAddress.Any, 21);
            _listener.Start();
            _activeConnections = new List<ClientConnection>();

            while (true)
            {
                HandleAcceptTcpClient(await _listener.AcceptTcpClientAsync());
            }
        }

        public void Stop()
        {
            _listener.Stop();
            _listener = null;            
        }

        private void HandleAcceptTcpClient(TcpClient client)
        {           
            ClientConnection connection = new ClientConnection(client);
            Task.Run(() => connection.HandleClient());            
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

