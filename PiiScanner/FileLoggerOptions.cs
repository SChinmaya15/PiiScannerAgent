using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PiiScanner
{
    public sealed class FileLoggerOptions
    {
        public string Directory { get; init; } = AppContext.BaseDirectory;
        public string FileNameFormat { get; init; } = "dd_MM'.txt'"; // produces 05_06.txt
        public Encoding Encoding { get; init; } = Encoding.UTF8;
    }

    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggerOptions _options;
        private readonly object _lock = new();
        private bool _disposed;

        public FileLoggerProvider(FileLoggerOptions? options = null)
        {
            _options = options ?? new FileLoggerOptions();
            Directory.CreateDirectory(_options.Directory);
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _options, _lock);

        public void Dispose()
        {
            _disposed = true;
        }

        private sealed class FileLogger : ILogger
        {
            private readonly string _category;
            private readonly FileLoggerOptions _options;
            private readonly object _lock;

            public FileLogger(string category, FileLoggerOptions options, object lockObj)
            {
                _category = category;
                _options = options;
                _lock = lockObj;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                if (formatter == null) return;

                var message = formatter(state, exception);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var level = logLevel.ToString();
                var exceptionText = exception is null ? string.Empty : Environment.NewLine + exception;
                var line = $"[{timestamp}] [{level}] [{_category}] {message}{exceptionText}{Environment.NewLine}";

                var fileName = DateTime.Now.ToString(_options.FileNameFormat);
                var path = Path.Combine(_options.Directory, fileName);

                lock (_lock)
                {
                    File.AppendAllText(path, line, _options.Encoding);
                }
            }
        }
    }
}