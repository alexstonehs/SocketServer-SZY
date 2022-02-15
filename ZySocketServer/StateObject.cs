using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace TYSocketServer
{
    class StateObject
    {
        public Socket WorkSocket;

        //public const int BufferSize = 1024;
        //public const int BufferSize = 20480;

        public byte[] Buffer;


        public StateObject(Socket socket, int bufferSize)
        {
            Buffer = new byte[bufferSize];
            WorkSocket = socket;
        }
    }
}
