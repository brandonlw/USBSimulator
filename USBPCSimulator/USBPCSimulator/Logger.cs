using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public static class Logger
  {
    public static LoggingLevel Level = LoggingLevel.Normal;

    public enum LoggingLevel
    {
      None = 0,
      Normal = 1,
      Verbose = 2,
      VeryVerbose = 3
    }

    public static event EventHandler<LogEventArgs> LogEntryAdded;

    public static void WriteLine(string message, LoggingLevel level)
    {
      if (Level >= level)
      {
        if (LogEntryAdded != null)
          LogEntryAdded(null, new LogEventArgs(message));
      }
    }
  }
}
