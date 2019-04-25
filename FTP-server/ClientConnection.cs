using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FTP_server
{
    class ClientConnection
    {


        #region Enums



        private enum DataConnectionType
        {
            Passive,
            Active,
        }


        #endregion

        private TcpListener _passiveListener;

        private TcpClient _controlClient;
        private TcpClient _dataClient;

        private NetworkStream _controlStream;
        private StreamReader _controlReader;
        private StreamWriter _controlWriter;


        private DataConnectionType _dataConnectionType = DataConnectionType.Active;


        private string _username;
       // private string _root;
        private string _currentDirectory;
        private IPEndPoint _dataEndpoint;
      //  private IPEndPoint _remoteEndPoint;

    
    

        public ClientConnection(TcpClient client)
        {
            _controlClient = client;

            _controlStream = _controlClient.GetStream();

            _controlReader = new StreamReader(_controlStream);
            _controlWriter = new StreamWriter(_controlStream);
        }

        public void HandleClient(object obj)
        {
            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();
            _currentDirectory = "/";
            string line;
            _dataClient = new TcpClient();
            try
            {
                while (!string.IsNullOrEmpty(line = _controlReader.ReadLine()))
                {
                    string response = null;

                    string[] command = line.Split(' ');

                    string cmd = command[0].ToUpperInvariant();
                    string arguments = command.Length > 1 ? line.Substring(command[0].Length + 1) : null;

                    if (string.IsNullOrWhiteSpace(arguments))
                        arguments = null;

                    if (response == null)
                    {
                        switch (cmd)
                        {
                            case "USER":
                                response = User(arguments);
                                break;
                            case "PASS":
                                response = Password(arguments);
                                break;
                            //case "CWD":
                            //    response = ChangeWorkingDirectory(arguments);
                            //    break;
                            //case "CDUP":
                            //    response = ChangeWorkingDirectory("..");
                            //    break;
                            case "PWD":
                                response = "257 \"/\" is current directory.";
                                break;
                            case "QUIT":
                                response = "221 Service closing control connection";
                                break;
                            case "TYPE":

                                response = "200 OK";
                                break;
                            case "PORT":
                                response = Port(arguments);
                                break;
                            case "PASV":
                                response = Passive();
                                break;
                            case "LIST":
                                response = List(arguments);
                                break;

                            default:
                                response = "502 Command not implemented";
                                break;
                        }
                    }

                    if (_controlClient == null || !_controlClient.Connected)
                    {
                        break;
                    }
                    else
                    {
                        _controlWriter.WriteLine(response);
                        _controlWriter.Flush();

                        if (response.StartsWith("221"))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw ;
            }
        }



        private void DoList(IAsyncResult result)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                _dataClient.EndConnect(result);
            }
            else
            {
                _dataClient = _passiveListener.EndAcceptTcpClient(result);
            }

            string pathname = (string)result.AsyncState;
            using (NetworkStream dataStream = _dataClient.GetStream())
            {
              var  _dataReader = new StreamReader(dataStream, Encoding.ASCII);
              var  _dataWriter = new StreamWriter(dataStream, Encoding.ASCII);
                IEnumerable<string> directories = Directory.EnumerateDirectories(pathname);

                foreach (string dir in directories)
                {
                    DirectoryInfo d = new DirectoryInfo(dir);

                    string date = d.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                        d.LastWriteTime.ToString("MMM dd  yyyy") :
                        d.LastWriteTime.ToString("MMM dd HH:mm");

                    string line = string.Format("drwxr-xr-x   1 owner   group {0,8} {1} {2}", "4096", date, d.Name);

                    _dataWriter.WriteLine(line);
                    _dataWriter.Flush();
                }
                IEnumerable<string> files = Directory.EnumerateFiles(pathname);

                foreach (string file in files)
                {
                    FileInfo f = new FileInfo(file);

                    string date = f.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                        f.LastWriteTime.ToString("MMM dd  yyyy") :
                        f.LastWriteTime.ToString("MMM dd HH:mm");

                    string line = string.Format("drwxr-xr-x   1 owner   group {0,8} {1} {2}", f.Length, date, f.Name);

                    _dataWriter.WriteLine(line);
                    _dataWriter.Flush();
                }
                _dataClient.Close();
                _dataClient = null;

                _controlWriter.WriteLine("226 Transfer complete");
                _controlWriter.Flush();
            }
        }
            #region FTP Commands
            private string List(string pathname)
        {
            if (pathname == null)
            {
                pathname = string.Empty;
            }

            pathname = new DirectoryInfo(Path.Combine(_currentDirectory, pathname)).FullName;

            
                if (_dataConnectionType == DataConnectionType.Active)
                {
                    _dataClient = new TcpClient();
                    _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoList, pathname);
                }
                else
                {
                    _passiveListener.BeginAcceptTcpClient(DoList, pathname);
                }

                return string.Format("150 Opening {0} mode data transfer for LIST", _dataConnectionType);
            

          //  return "450 Requested file action not taken";
        }



            private string Passive()
        {
            _dataConnectionType = DataConnectionType.Passive;
            IPAddress localAddress = ((IPEndPoint)_controlClient.Client.LocalEndPoint).Address;

            _passiveListener = new TcpListener(localAddress, 0);
            _passiveListener.Start();
            IPEndPoint localEndpoint = ((IPEndPoint)_passiveListener.LocalEndpoint);

            byte[] address = localEndpoint.Address.GetAddressBytes();
            short port = (short)localEndpoint.Port;

            byte[] portArray = BitConverter.GetBytes(port);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(portArray);

            return string.Format("227 Entering Passive Mode ({0},{1},{2},{3},{4},{5})",
                          address[0], address[1], address[2], address[3], portArray[0], portArray[1]);
        }


        private string Port(string argPort)
        {
            _dataConnectionType = DataConnectionType.Active;
            string[] ipAndPort = argPort.Split(',');

            byte[] ipAddress = new byte[4];
            byte[] port = new byte[2];

            for (int i = 0; i < 4; i++)
            {
                ipAddress[i] = Convert.ToByte(ipAndPort[i]);
            }

            for (int i = 4; i < 6; i++)
            {
                port[i - 4] = Convert.ToByte(ipAndPort[i]);
            }

            if (BitConverter.IsLittleEndian)
                Array.Reverse(port);
            _dataEndpoint = new IPEndPoint(new IPAddress(ipAddress), BitConverter.ToInt16(port, 0));
            return "200 Data Connection Established";
        }

        private string User(string username)
        {
            _username = username;

            return "331 Username ok, need password";
        }

        private string Password(string password)
        {
            if (true)
            {
                return "230 User logged in";
            }
            else
            {
                return "530 Not logged in";
            }
        }

        private string ChangeWorkingDirectory(string pathname)
        {
            return "250 Changed to new directory";
        }

        #endregion
    }
}
