using System;
using System.IO;
using System.Text;

namespace MechKit.Core
{
    /// <summary>
    /// 极简日志。写入 %LOCALAPPDATA%\MechKit\logs，
    /// 同时通过 <see cref="Message"/> 事件推送给任务面板。
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        /// <summary>日志事件，参数为已格式化的单行文本。</summary>
        public static event Action<string> Message;

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        public static string CurrentLogFile
        {
            get
            {
                return Path.Combine(AppPaths.Logs,
                    string.Format("MechKit-{0:yyyyMMdd}.log", DateTime.Now));
            }
        }

        private static void Write(string level, string message, Exception ex)
        {
            var line = string.Format("{0:HH:mm:ss} [{1}] {2}", DateTime.Now, level, message);
            if (ex != null)
            {
                line += Environment.NewLine + ex;
            }

            try
            {
                lock (Gate)
                {
                    AppPaths.Ensure(AppPaths.Logs);
                    File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响插件主流程
            }

            var handler = Message;
            if (handler != null)
            {
                try
                {
                    handler(line);
                }
                catch
                {
                    // 忽略订阅方的异常
                }
            }
        }
    }
}
