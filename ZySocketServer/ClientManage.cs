using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace TYSocketServer
{
    class ClientManage:IDisposable
    {
        /// <summary>
        /// 连接地址列表
        /// </summary>
        private readonly ConcurrentDictionary<string, ClientConnection> _connSockets;

        /// <summary>
        /// 站点连接被清理事件委托
        /// </summary>
        /// <param name="operationResultStr"></param>
        public delegate void ConnSocketRemove(string operationResultStr, string remoteEndPoint);
        /// <summary>
        /// 站点连接被清理事件
        /// </summary>
        public event ConnSocketRemove ConnSocketRemoved;

        public delegate void TransMngException(string exMessage);

        public event TransMngException TransMngError;

        public delegate void DeviceOnline(string mn);
        /// <summary>
        /// 设备上线事件
        /// </summary>
        public event DeviceOnline Online;

        public delegate void DeviceOffline(string mn);
        /// <summary>
        /// 设备下线事件
        /// </summary>
        public event DeviceOffline Offline;

        private bool _running = false;

        /// <summary>
        /// 连接对象闲置时间
        /// </summary>
        private readonly int _idleMin;
        public ClientManage(int idleCleanMinutes)
        {
            _idleMin = idleCleanMinutes < 1 ? 1 : idleCleanMinutes;
            _connSockets = new ConcurrentDictionary<string, ClientConnection>();
            _running = true;
            Thread t = new Thread(CleanConnAction){IsBackground = true};
            t.Start();
            
        }

        /// <summary>
        /// 更新Socket连接信息
        /// </summary>
        /// <param name="mn"></param>
        /// <param name="connSocket"></param>
        public string UpdateDeviceSockets(string mn, Socket connSocket)
        {
            string reStr;
            if (_connSockets.TryGetValue(mn, out ClientConnection currentConn))
            {
                if (!currentConn.ClientSocket.Connected ||
                    currentConn.ClientSocket.RemoteEndPoint != connSocket.RemoteEndPoint)
                {
                    try
                    {
                        currentConn.ClientSocket.Shutdown(SocketShutdown.Both);
                        currentConn.ClientSocket.Close();
                    }
                    catch (Exception e)
                    {
                        ErrorMsg($"更新移除Socket连接失败:{e.Message}\r\n");
                    }
                    bool updateSuccess = _connSockets.TryUpdate(mn,
                        new ClientConnection(DateTime.Now) { ClientSocket = connSocket }, currentConn);

                    reStr = $"站点[{mn}]通讯地址更新{(updateSuccess ? "成功" : "失败")}";

                }
                else
                {
                    bool updateTimeSuccess = _connSockets.TryUpdate(mn,
                        new ClientConnection(DateTime.Now) { ClientSocket = _connSockets[mn].ClientSocket },
                        _connSockets[mn]);
                    reStr = $"站点[{mn}]通讯时间更新{(updateTimeSuccess ? "成功" : "失败")}";
                }
            }
            else
            {
                bool addSuccess = _connSockets.TryAdd(mn, new ClientConnection(DateTime.Now) { ClientSocket = connSocket });
                reStr = $"站点[{mn}]通讯地址添加{(addSuccess ? "成功" : "失败")}";
                Online?.Invoke(mn);
            }
            return reStr;
        }
        /// <summary>
        /// 清理Socket连接，如指定分钟内未通讯则断开连接
        /// </summary>
        private void CleanDeviceSockets()
        {
            DateTime dt = DateTime.Now;
            foreach (string key in _connSockets.Keys)
            {
                string remoteEndPoint = string.Empty;
                string mn = string.Empty;
                if (_connSockets.TryGetValue(key, out ClientConnection socketInfo))
                {
                    TimeSpan span = dt.Subtract(socketInfo.AccessTime);
                    if (span.TotalMinutes > _idleMin)
                    {
                        try
                        {
                            remoteEndPoint = socketInfo.SocketRemoteEndPoint;
                            mn = key;
                            socketInfo.ClientSocket.Shutdown(SocketShutdown.Both);
                            socketInfo.ClientSocket.Close();
                            socketInfo.ClientSocket.Dispose();
                        }
                        catch (Exception e)
                        {
                            ErrorMsg($"清理Socket连接失败:{e.Message}\r\n");
                        }
                        finally
                        {
                            bool isRemoved = _connSockets.TryRemove(key, out _);
                            GetConnRemovedStrInfo(mn, remoteEndPoint, isRemoved);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 移除指定的客户端连接
        /// </summary>
        /// <param name="connSocketId">客户端连接唯一ID</param>
        internal bool RemoveDeviceConn(string connSocketId, out string msg)
        {
            string remoteEndPoint = string.Empty;
            string mn = string.Empty;
            if (_connSockets.TryGetValue(connSocketId, out ClientConnection socketInfo))
            {
                try
                {
                    remoteEndPoint = socketInfo.SocketRemoteEndPoint;
                    mn = connSocketId;
                    socketInfo.ClientSocket.Shutdown(SocketShutdown.Both);
                    socketInfo.ClientSocket.Close();
                    socketInfo.ClientSocket.Dispose();
                    msg = $"[{DateTime.Now}] - 客户端{connSocketId}已移除";
                    return true;
                }
                catch (Exception e)
                {
                    ErrorMsg($"移除Socket连接失败:{e.Message}\r\n");
                    msg = $"[{DateTime.Now}] - 客户端{connSocketId}移除失败：{e.Message}";
                    return false;
                }
                finally
                {
                    bool isRemoved = _connSockets.TryRemove(mn, out _);
                    GetConnRemovedStrInfo(mn, remoteEndPoint, isRemoved);
                }
            }
            msg = $"[{DateTime.Now}] - 客户端{connSocketId}未找到";
            return false;
        }
        /// <summary>
        /// 获取当前客户端连接数
        /// </summary>
        /// <returns></returns>
        internal int GetCurrentConnNum()
        {
            return _connSockets.Count;
        }
        /// <summary>
        /// 广播消息至所有客户端
        /// </summary>
        /// <param name="command"></param>
        internal void BroadcastingToClients(byte[] command)
        {
            if (_connSockets == null)
                return;
            foreach (KeyValuePair<string, ClientConnection> kv in _connSockets)
            {
                try
                {
                    kv.Value.ClientSocket.Send(command);
                }
                catch (Exception e)
                {
                    ErrorMsg($"广播消息异常：{e.Message}");
                }
            }
        }

        internal bool SendMessageToClient(string clientId, byte[] data, out string msg)
        {
            if (_connSockets == null)
            {
                msg = $"clientId:{clientId} 数据发送失败 连接地址集合未初始化";
                return false;
            }
                
            if (_connSockets.TryGetValue(clientId, out ClientConnection conn))
            {
                try
                {
                    conn.ClientSocket.Send(data);
                    msg = $"clientId:{clientId} 数据下发完成";
                    return true;
                }
                catch (Exception e)
                {
                    msg = $"clientId:{clientId} 发送消息异常：{e.Message}";
                    //ErrorMsg($"发送消息异常：{e.Message}");
                    return false;
                }
            }
            msg = $"clientId:{clientId} 对应连接不存在，无法下发数据";
            return false;
        }

    
        void CleanConnAction()
        {
            while (_running)
            {
                try
                {
                    CleanDeviceSockets();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                Thread.Sleep(TimeSpan.FromMinutes(_idleMin));
            }
         
        }

        private void ErrorMsg(string errorTxt)
        {
            TransMngError?.Invoke(errorTxt);
        }

        private void GetConnRemovedStrInfo(string mn, string remoteEndPoint, bool removeSucceeded)
        {
            string reStr = $"站点[{mn}]断开连接{(removeSucceeded ? "成功" : "失败")}\r\n";
            ConnSocketRemoved?.Invoke(reStr, remoteEndPoint);
            Offline?.Invoke(mn);
        }

        public void Dispose()
        {
            _running = false;
        }
    }
}
