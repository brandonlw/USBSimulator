using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public class LogEventArgs : EventArgs
  {
    public string Message { get; internal set; }

    public LogEventArgs(string message)
    {
      Message = message;
    }
  }
}
