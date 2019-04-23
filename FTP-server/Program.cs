using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FTP_server
{
    class Program
    {
       // FtpServer server;
        
        static void Main(string[] args)
        {

            using (FtpServer server = new FtpServer())
            {
                server.Start();

                Console.WriteLine("Press any key to stop...");
                Console.ReadKey(true);
            }
        }

  
        //  public List<FtpClient> clients;
    }
}
