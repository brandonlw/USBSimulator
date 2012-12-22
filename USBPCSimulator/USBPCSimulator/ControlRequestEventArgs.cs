using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public class ControlRequestEventArgs : EventArgs
  {
    public int bmRequestType { get; set; }
    public int bRequest { get; set; }
    public int wValue { get; set; }
    public int wIndex { get; set; }
    public int wLength { get; set; }
    public bool Ignore { get; set; }
    public bool Stall { get; set; }
    public byte[] AttachedData { get; set; }
    public byte[] ReturnData { get; set; }

    public bool CanIgnore
    {
      get
      {
        return ((bmRequestType & 0x80) > 0) | (wLength == 0);
      }
    }

    public ControlRequestEventArgs(int bmRequestType, int bRequest,
      int wValue, int wIndex, int wLength, byte[] attachedData)
    {
      this.bmRequestType = bmRequestType;
      this.bRequest = bRequest;
      this.wValue = wValue;
      this.wIndex = wIndex;
      this.wLength = wLength;
      this.AttachedData = attachedData;

      //Ignore by default
      this.Ignore = true;
    }
  }
}
