using System;
using System.IO;
using System.Text;

namespace HSGFrame.Logging
{
    /// <summary>写进文件的落点，按行追加，UTF-8 无 BOM。</summary>
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        private readonly LogFormatOptions _options;
        private readonly StreamWriter _writer;
        private bool _isDisposed;

        /// <summary>用文件路径与格式选项构造，父目录不存在时先建。</summary>
        /// <param name="filePath">目标文件路径。</param>
        /// <param name="options">格式选项，null 按全部关闭处理。</param>
        public FileLogSink(string filePath, LogFormatOptions options)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            FilePath = filePath;
            _options = options ?? new LogFormatOptions();

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 打开失败（比如路径不可写、磁盘只读）时降级为丢弃：日志写不进去不该把业务打挂。
            // 只接住 IOException 与 UnauthorizedAccessException，其余异常照常抛出——那多半是编程错误而非环境问题。
            try
            {
                var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            }
            catch (IOException)
            {
                _writer = null;
            }
            catch (UnauthorizedAccessException)
            {
                _writer = null;
            }
        }

        /// <summary>目标文件路径。</summary>
        public string FilePath { get; }

        /// <summary>写一条日志；写不进磁盘时安静跳过那一条。</summary>
        /// <param name="entry">要写的日志。</param>
        public void Write(LogEntry entry)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                _writer.WriteLine(entry.Format(_options));
            }
            catch (IOException)
            {
                // 磁盘满、文件被占用等：静默丢弃这一条，别让日志把业务打挂。
            }
            catch (UnauthorizedAccessException)
            {
                // 写的过程中权限被收回：同样静默丢弃。
            }
        }

        /// <summary>关闭底层文件句柄。</summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _writer?.Dispose();
        }
    }
}
