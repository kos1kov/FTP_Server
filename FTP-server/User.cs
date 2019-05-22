using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FTP_server
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public float Hash { get; set; }
        public string homedir { get; set; }
    }
}
