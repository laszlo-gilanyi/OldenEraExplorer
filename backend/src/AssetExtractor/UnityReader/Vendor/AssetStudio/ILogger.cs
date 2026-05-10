using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    internal enum LoggerEvent
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error,
    }

    internal interface ILogger
    {
        void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false);
    }

    internal sealed class DummyLogger : ILogger
    {
        public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel) { }
    }
}
