using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public class IncomingDataEventArgs : EventArgs
  {
    public int Endpoint { get; internal set; }
    public byte[] Data { get; internal set; }

    public IncomingDataEventArgs(int endpoint, byte[] data)
    {
      Endpoint = endpoint;
      Data = data;
    }
  }
}
