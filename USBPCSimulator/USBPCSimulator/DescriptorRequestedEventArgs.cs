using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public class DescriptorRequestedEventArgs : EventArgs
  {
    public int wValue { get; set; }
    public int wIndex { get; set; }
    public byte[] DescriptorData { get; set; }

    public DescriptorRequestedEventArgs(int wValue, int wIndex)
    {
      this.wValue = wValue;
      this.wIndex = wIndex;
    }
  }
}
