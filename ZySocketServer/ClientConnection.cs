using System;
using System.Net.Sockets;

namespace TYSocketServer
{
    public class ClientConnection
    {
        public ClientConnection(DateTime dt)
        {
            AccessTime = dt;
        }
        /// <summary>
        /// Socket对象信息
        /// </summary>
        public Socket ClientSocket
        {
            get => _socket;
            set
            {
                _socket = value;
                SocketRemoteEndPoint = _socket.RemoteEndPoint.ToString();
            }
        }

        private Socket _socket;
        /// <summary>
        /// 访问时间
        /// </summary>
        public DateTime AccessTime { get; set; }
        /// <summary>
        /// Socket RemoteEndPoint
        /// </summary>
        public string SocketRemoteEndPoint { get; private set; }
    }
}
