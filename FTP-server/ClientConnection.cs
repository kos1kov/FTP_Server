using System;
using System.Collections.Generic;
using System.Globalization;
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
        private enum TransferType
        {
            Ascii,
            Ebcdic,
            Image,
            Local,
        }
        #endregion

        private TcpListener _passiveListener;
        private TransferType _connectionType = TransferType.Ascii;
        private TcpClient _controlClient;
        private TcpClient _dataClient;
        private NetworkStream _controlStream;
        private StreamReader _controlReader;
        private StreamWriter _controlWriter;
        private string _root;
        private string _storage = "C:\\FTP";
        private string _username;
        private float _password;
        private string _currentDirectory;
        private IPEndPoint _dataEndpoint;
        


        public ClientConnection(TcpClient client)
        {
            _controlClient = client;
            _controlStream = _controlClient.GetStream();
            _controlReader = new StreamReader(_controlStream);
            _controlWriter = new StreamWriter(_controlStream);
        }

        public async Task HandleClient()
        {
            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();

          //  _currentDirectory = "C:\\FTP";
           
            string line;
            string renameFrom = null;
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
                            case "CWD":
                                response = ChangeWorkingDirectory(arguments);
                                break;
                            case "CDUP":
                                response = ChangeWorkingDirectory("..");
                                break;
                            case "PWD":
                                response = PrintWorkingDirectory();
                                break;
                            case "QUIT":
                                response = "221 Service closing control connection";
                                break;
                            case "TYPE":
                                response = Type(command[1], command.Length == 3 ? command[2] : null);
                                response = "200 OK";
                                break;
                            case "RNFR":
                                renameFrom = arguments;
                                response = "350 Requested file action pending further information";
                                break;
                            case "RNTO":
                                response = Rename(renameFrom, arguments);
                                break;
                            case "DELE":
                                response = Delete(arguments);
                                break;
                            case "RMD":
                                response = RemoveDir(arguments);
                                break;
                            case "MKD":
                                response = CreateDir(arguments);
                                break;
                            case "PORT":
                                response = Port(arguments);
                                break;
                            case "STOR":
                                response = await Store(arguments);
                                break;
                            case "PASV":
                                response = Passive();
                                break;
                            case "LIST":
                                response = await List(arguments);
                                break;
                            case "RETR":
                                response = await Retrieve(arguments);
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
                throw;
            }
        }

        private string Registration()
        {
            Directory.CreateDirectory(Path.Combine(_storage, _username));
            User user = new User { Name = _username, Hash = _password, homedir = Path.Combine(_storage, _username) };
            FtpServer.db.Users.Add(user);
            FtpServer.db.SaveChanges();
            return "200 ok";
        }

        private bool IsPathValid(string path)
        {
            return path.StartsWith(_root);
        }

        private string NormalizeFilename(string path)
        {
            if (path == null)
            {
                path = string.Empty;
            }

            if (path == "/")
            {
                return _root;
            }
            else if (path.StartsWith("/"))
            {
                path = new FileInfo(Path.Combine(_root, path.Substring(1))).FullName;
            }
            else
            {
                path = new FileInfo(Path.Combine(_currentDirectory, path)).FullName;
            }

            return IsPathValid(path) ? path : null;
        }


        private void DoList(TcpClient _dataClient, string pathname)
        {

            using (NetworkStream dataStream = _dataClient.GetStream())
            {
                
                var _dataWriter = new StreamWriter(dataStream, Encoding.ASCII);
                IEnumerable<string> directories = Directory.EnumerateDirectories(pathname);

                foreach (string dir in directories)
                {
                    DirectoryInfo d = new DirectoryInfo(dir);

                    string line = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0:MM-dd-yy  hh:mmtt}       {1,-14} {2}",
                                    d.LastWriteTime,
                                    "<DIR>",
                                    d.Name);

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

                    string line = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0:MM-dd-yy  hh:mmtt} {1,20} {2}",
                                    f.LastWriteTime,
                                    f.Length,
                                    f.Name);

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
        private async Task<string> Store(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                var client = await _passiveListener.AcceptTcpClientAsync();
                DoStore(client, pathname);

                return string.Format("226 Closing data connection, file transfer successful");
            }

            return "450 Requested file action not taken";
        }
        private void DoStore(TcpClient _dataClient, string pathname)
        {

            using (NetworkStream dataStream = _dataClient.GetStream())
            {
                long bytes = 0;

                using (FileStream fs = new FileStream(pathname, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                {
                    bytes = CopyStream(dataStream, fs);
                }
            }
        }


        private async Task<string> Retrieve(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (IsPathValid(pathname))
            {
                if (File.Exists(pathname))
                {

                    var client = await _passiveListener.AcceptTcpClientAsync();
                    DoRetrieve(client,pathname);


                    return string.Format("150");
                }
            }

            return "550 File Not Found";
        }
        private static long CopyStream(Stream input, Stream output, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int count = 0;
            long total = 0;

            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, count);
                total += count;
            }

            return total;
        }
        private long CopyStream(Stream input, Stream output)
        {
            if (_connectionType == TransferType.Image)
            {
                return CopyStream(input, output, 4096);
            }
            else
            {
                return CopyStreamAscii(input, output, 4096);
            }
        }
        private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
        {
            char[] buffer = new char[bufferSize];
            int count = 0;
            long total = 0;

            using (StreamReader rdr = new StreamReader(input, Encoding.ASCII))
            {
                using (StreamWriter wtr = new StreamWriter(output, Encoding.ASCII))
                {
                    while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        wtr.Write(buffer, 0, count);
                        total += count;
                    }
                }
            }

            return total;
        }
        private void DoRetrieve(TcpClient _dataClient, string pathname)
        {

            using (NetworkStream dataStream = _dataClient.GetStream())
            {
                using (FileStream fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
                {
                    CopyStream(fs, dataStream);
                    _dataClient.Close();
                    _dataClient = null;
                    _controlWriter.WriteLine("226 Closing data connection, file transfer successful");
                    _controlWriter.Flush();
                }
            }
        }


        private async Task<string> List(string pathname)
        {
            pathname = NormalizeFilename(pathname);
            if (pathname == null)
            {
                pathname = string.Empty;
            }

            pathname = new DirectoryInfo(Path.Combine(_currentDirectory, pathname)).FullName;

            var client = await _passiveListener.AcceptTcpClientAsync();

            DoList(client, pathname);

            return string.Format("150");

        }



        private string Passive()
        {

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
        private string Type(string typeCode, string formatControl)
        {
            switch (typeCode.ToUpperInvariant())
            {
                case "A":
                    _connectionType = TransferType.Ascii;
                    break;
                case "I":
                    _connectionType = TransferType.Image;
                    break;
                default:
                    return "504 Command not implemented for that parameter";
            }

            if (!string.IsNullOrWhiteSpace(formatControl))
            {
                return "504 Command not implemented for that parameter";
            }

            return string.Format("200 Type set to {0}", _connectionType);
        }
        private string ChangeWorkingDirectory(string pathname)
        {
            if (pathname == "/")
            {
                _currentDirectory = _root;
            }
            else
            {
                string newDir;

                if (pathname.StartsWith("/"))
                {
                    pathname = pathname.Substring(1).Replace('/', '\\');
                    newDir = Path.Combine(_root, pathname);
                }
                else
                {
                    pathname = pathname.Replace('/', '\\');
                    newDir = Path.Combine(_currentDirectory, pathname);
                }

                if (Directory.Exists(newDir))
                {
                    _currentDirectory = new DirectoryInfo(newDir).FullName;

                    if (!IsPathValid(_currentDirectory))
                    {
                        _currentDirectory = _root;
                    }
                }
                else
                {
                    _currentDirectory = _root;
                }
            }

            return "250 Changed to new directory";
        }
        private string PrintWorkingDirectory()
        {
            string current = _currentDirectory.Replace(_root, string.Empty).Replace('\\', '/');

            if (current.Length == 0)
            {
                current = "/";
            }

            return string.Format("257 \"{0}\" is current directory.", current); ;
        }
        private string User(string username)
        {
            _username = username;

            return "331 Username ok, need password";
        }

        private string Password(string password)
        {
            _password = password.GetHashCode();
            var users = FtpServer.db.Users.FirstOrDefault(x => x.Hash == _password);
            if (users != null)
            {
                _root = users.homedir;
                _currentDirectory = _root;
                return "230 User logged in";
            }
            else
            {
                Registration();
                _root = Path.Combine(_storage, _username);
                _currentDirectory = _root;
                return "230 User logged in";
            }
        }
        private string Rename(string renameFrom, string renameTo)
        {
            if (string.IsNullOrWhiteSpace(renameFrom) || string.IsNullOrWhiteSpace(renameTo))
            {
                return "450 Requested file action not taken";
            }

            renameFrom = NormalizeFilename(renameFrom);
            renameTo = NormalizeFilename(renameTo);

            if (renameFrom != null && renameTo != null)
            {
                if (File.Exists(renameFrom))
                {
                    File.Move(renameFrom, renameTo);
                }
                else if (Directory.Exists(renameFrom))
                {
                    Directory.Move(renameFrom, renameTo);
                }
                else
                {
                    return "450 Requested file action not taken";
                }

                return "250 Requested file action okay, completed";
            }

            return "450 Requested file action not taken";
        }
        private string Delete(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (File.Exists(pathname))
                {
                    File.Delete(pathname);
                }
                else
                {
                    return "550 File Not Found";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 File Not Found";
        }

        private string RemoveDir(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (Directory.Exists(pathname))
                {
                    Directory.Delete(pathname);
                }
                else
                {
                    return "550 Directory Not Found";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 Directory Not Found";
        }
        private string CreateDir(string pathname)
        {
            pathname = NormalizeFilename(pathname);

            if (pathname != null)
            {
                if (!Directory.Exists(pathname))
                {
                    Directory.CreateDirectory(pathname);
                }
                else
                {
                    return "550 Directory already exists";
                }

                return "250 Requested file action okay, completed";
            }

            return "550 Directory Not Found";
        }
        #endregion
    }
}
