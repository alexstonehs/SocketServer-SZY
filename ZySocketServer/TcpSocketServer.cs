using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TYSocketServer
{
    public class TcpSocketServer
    {
        /// <summary>
        /// 监听地址（本机）
        /// </summary>
        private readonly string _listeningIp;
        /// <summary>
        /// 监听端口（本机）
        /// </summary>
        private readonly int _listeningPort;

        private IPEndPoint _localEndPoint;

        private Socket _listener;

        /// <summary>
        /// 接收传入Socket连接线程
        /// </summary>
        private Thread _acceptThread;

        private bool _isSocketBind;

        public ManualResetEvent AllDone = new ManualResetEvent(false);

        private ClientManage _clientManage;
        /// <summary>
        /// Server数据接收委托
        /// </summary>
        /// <param name="clientInfo"></param>
        /// <param name="data"></param>
        public delegate void ServerDataReceive(string clientInfo, byte[] data);

        public delegate void ServerMessage(ConnClient clientInfo);
        /// <summary>
        /// Socket Server消息通知事件
        /// </summary>
        public event ServerMessage ServerMessageEvent;
        /// <summary>
        /// Socket Server数据接收事件
        /// </summary>
        public event ServerDataReceive ServerDataReceivedEvent;
        /// <summary>
        /// Socket客户端数据缓存大小
        /// </summary>
        private readonly int _clientBufferSize = 0;

        private ConcurrentQueue<TriggerData> _triggerQueue;

        /// <summary>
        /// 初始化Socket Server
        /// </summary>
        /// <param name="serverListeningIp"></param>
        /// <param name="port"></param>
        /// <param name="bufferSize"></param>
        public TcpSocketServer(string serverListeningIp, int port, int bufferSize = 1024)
        {
            _listeningIp = serverListeningIp;
            _listeningPort = port;
            if (_listeningPort == default)
            {
                _listeningPort = 6050;
            }

            _clientBufferSize = bufferSize;
            _triggerQueue = new ConcurrentQueue<TriggerData>();
            RunTrigger();
        }
        /// <summary>
        /// 开始监听
        /// </summary>
        /// <param name="connIdleMin">连接客户端通讯超时时间，超时将执行清理动作。默认为2分钟</param>
        public void StartServer(int connIdleMin = 2)
        {
            InitServer(connIdleMin);
        }
        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopServer()
        {
            _listener?.Disconnect(false);
            _listener?.Close();
            _isSocketBind = false;
            AllDone.Set();
            _clientManage?.Dispose();
        }
        /// <summary>
        /// 移除客户端
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="message">移除客户端操作信息</param>
        /// <returns></returns>
        public bool RemoveClient(string clientId, out string message)
        {
            bool? removed = false;
            string msg = string.Empty;
            removed = _clientManage?.RemoveDeviceConn(clientId, out msg);
            message = msg;
            if (removed == null)
                return false;
            return (bool)removed;
        }
        /// <summary>
        /// 获取客户端连接数
        /// </summary>
        /// <returns></returns>
        public int GetClientConnectionCount()
        {
            return _clientManage.GetCurrentConnNum();
        }

        private void ClientManageOnOffline(string mn)
        {
            ServerMessageEvent?.Invoke(new ConnClient
            {
                ClientId = mn, EventType = ServerEvent.Offline, Message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]站点{mn} 下线"
            });
        }

        private void ClientManageOnOnline(string mn)
        {
            ServerMessageEvent?.Invoke(new ConnClient
            {
                ClientId = mn, EventType = ServerEvent.Online, Message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]站点{mn} 上线"
            });
        }

        private void ClientManageOnTransMngError(string exMessage)
        {
            ServerMessageEvent?.Invoke(new ConnClient
            {
                EventType = ServerEvent.Error,
                Message = exMessage
            });
        }

        private void ClientManageOnConnSocketRemoved(string operationResultStr, string remoteEndpoint)
        {
            Console.WriteLine($"客户端:{remoteEndpoint} {operationResultStr}");
            ServerMessageEvent?.Invoke(new ConnClient
            {
                ClientId = remoteEndpoint,
                EventType = ServerEvent.Other,
                Message = $"客户端:{remoteEndpoint} {operationResultStr}"
            });
        }
        /// <summary>
        /// 广播消息
        /// </summary>
        /// <param name="msg">消息字符串</param>
        /// <param name="encoding">编码规则</param>
        public void BroadCastingMessage(string msg, Encoding encoding)
        {
            _clientManage?.BroadcastingToClients(encoding.GetBytes(msg));
        }
        /// <summary>
        /// 广播消息
        /// </summary>
        /// <param name="data">要广播的数据</param>
        public void BroadCastingMessage(byte[] data)
        {
            _clientManage?.BroadcastingToClients(data);
        }
        /// <summary>
        /// 发送消息至指定客户端
        /// </summary>
        /// <param name="clientId">客户端id，通过连接事件获取</param>
        /// <param name="msg">要发送的字符串消息</param>
        /// <param name="encoding">编码规则</param>
        public (bool, string) SendMessageToClient(string clientId, string msg, Encoding encoding)
        {
            string resMsg = null;
            bool? b = _clientManage?.SendMessageToClient(clientId, encoding.GetBytes(msg), out resMsg);
            if (b == null)
            {
                return (false,"Server客户端类别未初始化");
            }
            return ((bool)b, resMsg);
        }
        /// <summary>
        /// 发送消息至指定客户端
        /// </summary>
        /// <param name="clientId">客户端id，通过连接事件获取</param>
        /// <param name="data">要发送的数据</param>
        public (bool, string) SendMessageToClient(string clientId, byte[] data)
        {
            string resMsg = null;
            bool? b = _clientManage?.SendMessageToClient(clientId, data, out resMsg);
            if(b == null)
            {
                return (false, "Server客户端类别未初始化");
            }
            return ((bool)b, resMsg);
        }

        private void InitServer(int connIdleTime)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(_listeningIp, out ip))
            {
                Console.WriteLine("IP地址设置有误，请检查");
                return;
            }
            var ipAddress = IPAddress.Parse(_listeningIp);
            _localEndPoint = new IPEndPoint(ipAddress, _listeningPort);

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _acceptThread = new Thread(AcceptFun);
            _acceptThread.Start();

            _clientManage = new ClientManage(connIdleTime);
            _clientManage.ConnSocketRemoved += ClientManageOnConnSocketRemoved;
            _clientManage.TransMngError += ClientManageOnTransMngError;
            _clientManage.Online += ClientManageOnOnline;
            _clientManage.Offline += ClientManageOnOffline;
        }

        private void AcceptFun()
        {
            try
            {
                _listener.Bind(_localEndPoint);
                _listener.Listen(150);
                _isSocketBind = true;
            }
            catch (SocketException se)
            {
                if (se.ErrorCode == 10049)
                {
                    Console.WriteLine("Socket监听地址或端口错误\r\n");
                    return;
                }

                Console.WriteLine($"Socket监听地址或端口错误:{se.Message}\r\n");
            }
            catch (Exception ex)
            {

                Console.WriteLine(
                    $"{DateTime.Now.ToString(CultureInfo.InvariantCulture)},收到异常在AcceptFun中 {ex.Message}\r\n");
            }
            //listener.BeginSend()
            Console.WriteLine("Server监听已启动");
            while (_isSocketBind)
            {
                AllDone.Reset();
                try
                {
                    _listener.BeginAccept(AcceptCallbacks, _listener);
                }
                catch (SocketException)
                {
                    Console.WriteLine("Socket连接错误\r\n");
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    Console.WriteLine("Socket对象已被释放\r\n");
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"收到异常在AcceptFun中 {ex.Message + " " + ex.StackTrace}\r\n");
                    continue;
                }
                finally
                {
                    AllDone.WaitOne();
                }
            }

        }

        public void AcceptCallbacks(IAsyncResult ar)
        {
            try
            {
                AllDone.Set();
                Socket socket = (Socket)ar.AsyncState;
                if (socket != null)
                {
                    Socket handler = socket.EndAccept(ar);
                    Console.WriteLine(
                        $"收到连接 {((IPEndPoint)handler.RemoteEndPoint).Address}:{((IPEndPoint)handler.RemoteEndPoint).Port}\r\n");
                    var cid =
                        $"{((IPEndPoint) handler.RemoteEndPoint).Address}:{((IPEndPoint) handler.RemoteEndPoint).Port}";
                    ServerMessageEvent?.Invoke(new ConnClient
                    {
                        ClientId = cid,
                        EventType = ServerEvent.Connected,
                        Message = "客户端已连接"
                    });
                    _clientManage.UpdateDeviceSockets(cid, handler);
                    StateObject state = new StateObject( handler, _clientBufferSize);
                    handler.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, ReadCallback, state);
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Socket对象已被释放\r\n");
            }
            catch (SocketException se)
            {
                Console.WriteLine($"收到异常在AcceptCallback BeginReceive中 {se.Message + " " + se.StackTrace}\r\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"收到异常在AcceptCallback中 {ex.Message + " " + ex.StackTrace}\r\n");
            }
        }
        public void ReadCallback(IAsyncResult ar)
        {
            Socket handler = null;
            try
            {
                StateObject state = (StateObject) ar.AsyncState;
                handler = state.WorkSocket;
                if (handler == null)
                {
                    return;
                }

                var isHandlerAlive = IsSocketConnected(handler);
                if (!isHandlerAlive)
                {
                    try
                    {
                        var cid =
                            $"{((IPEndPoint)handler.RemoteEndPoint).Address}:{((IPEndPoint)handler.RemoteEndPoint).Port}";
                        string msg = string.Empty;
                        _clientManage?.RemoveDeviceConn(cid, out msg);
                        Console.WriteLine(msg);
                        handler.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"客户端移除失败：{e.Message}");
                    }
                    return;
                }


                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    var sInfo = handler.RemoteEndPoint.ToString();
                    _clientManage.UpdateDeviceSockets(sInfo, handler);
                    var data = state.Buffer.ToArray();
                    _triggerQueue?.Enqueue(new TriggerData{ClientId = sInfo, Data = data});
                    state = new StateObject(handler, _clientBufferSize);

                    try
                    {
                        if (IsSocketConnected(handler))
                        {
                            handler.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, ReadCallback, state);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"收到异常在ReadCallback BeginReceive中 {ex.Message + "  " + ex.StackTrace}\r\n");
                    }
                }
                else
                {
                    Console.WriteLine("aaa");
                }

            }
            catch (OutOfMemoryException ex)
            {
                Console.WriteLine($"收到异常在ReadCallback-oom {ex.Message + "  " + ex.StackTrace}\r\n");
                if (handler != null)
                {
                    try
                    {
                        var cid =
                            $"{((IPEndPoint)handler.RemoteEndPoint).Address}:{((IPEndPoint)handler.RemoteEndPoint).Port}";
                        string msg = string.Empty;
                        _clientManage?.RemoveDeviceConn(cid, out msg);
                        Console.WriteLine(msg);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"客户端移除失败：{e.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"收到异常在ReadCallback中 {ex.Message + "  " + ex.StackTrace}\r\n");
            }
        }

        void RunTrigger()
        {
            Thread t = new Thread(Run)
            {
                IsBackground = true
            };

            void Run()
            {
                while (true)
                {
                    if (_triggerQueue.TryDequeue(out var res))
                    {
                        ServerDataReceivedEvent?.Invoke(res.ClientId, res.Data);
                    }
                    Thread.Sleep(10);
                }
            }
            t.Start();
          
        }
        private bool IsSocketConnected(Socket skt)
        {
            try
            {
                var a = !(skt.Poll(1, SelectMode.SelectRead) && (skt.Available == 0));
                return a;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
        /// <summary>
        /// 数据转换，转换失败返回转换目标格式的默认值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="scValue"></param>
        /// <returns></returns>
        private static T ConvertValue<T>(string scValue)
        {
            Type type = typeof(T);
            if (string.IsNullOrEmpty(scValue))
            {
                return default(T);
            }
            try
            {
                if (!typeof(T).IsGenericType)
                {
                    var tmp = Convert.ChangeType(scValue, type);
                    return (T)tmp;
                }
                Type genericTypeDefinition = typeof(T).GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(Nullable<>))
                {
                    return (T)Convert.ChangeType(scValue, Nullable.GetUnderlyingType(typeof(T)) ?? throw new InvalidOperationException());
                }
                return default(T);
            }
            catch (Exception)
            {
                return default(T);
            }
        }
    }

    public class TriggerData
    {
        public string ClientId { get; set; }
        public byte[] Data { get; set; }
    }
}
