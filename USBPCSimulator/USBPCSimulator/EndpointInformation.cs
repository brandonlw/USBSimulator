using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public class EndpointInformation
  {
    public int Endpoint { get; internal set; }
    public EndpointDirection Direction { get; internal set; }
    public EndpointType Type { get; internal set; }
    public int MaximumPacketSize { get; internal set; }

    public enum EndpointDirection
    {
      Incoming = 0x80,
      Outgoing = 0x00
    }

    public enum EndpointType
    {
      Isochronous = 0x01,
      Bulk = 0x02,
      Interrupt = 0x03
    }

    public EndpointInformation(int endpoint, EndpointDirection direction, EndpointType type, int maxPacketSize)
    {
      Endpoint = endpoint;
      Direction = direction;
      Type = type;
      MaximumPacketSize = maxPacketSize;
    }
  }
}
