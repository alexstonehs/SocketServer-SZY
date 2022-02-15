using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TYSocketServer
{
    public enum ServerEvent
    {
        Offline = 0,
        Online = 1,
        Error = 2,
        Other = 3,
        Connected = 4
    }
}
