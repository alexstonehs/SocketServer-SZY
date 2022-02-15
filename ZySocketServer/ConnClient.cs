using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TYSocketServer
{
    /// <summary>
    /// 客户端消息对象
    /// </summary>
    public class ConnClient
    {
        /// <summary>
        /// 连接客户端识别ID
        /// </summary>
        public string ClientId { get; set; }
        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// 事件类型
        /// </summary>
        public ServerEvent EventType { get; set; }

    }
}
