using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using USBSimulator;

namespace USBSimulator.Devices
{
  //NOTE: This is kinda sorta untested. It works and all, but I haven't used it to actually
  //  do a loopback test or anything.
  public class SerialAdapter : IUSBDevice
  {
    public List<EndpointInformation> Endpoints { get; internal set; }
    private USBSimulatorDevice _device;

    public void OnInitialize(USBSimulatorDevice device)
    {
      _device = device;

      Endpoints = new List<EndpointInformation>();
      Endpoints.Add(new EndpointInformation(0x01, EndpointInformation.EndpointDirection.Outgoing,
        EndpointInformation.EndpointType.Bulk, 0x40));
      Endpoints.Add(new EndpointInformation(0x03, EndpointInformation.EndpointDirection.Incoming,
        EndpointInformation.EndpointType.Bulk, 0x40));
    }

    public void OnDescriptorRequested(DescriptorRequestedEventArgs e)
    {
      switch ((e.wValue >> 8) & 0xFF)
      {
        case 0x01: //Device descriptor
          {
            e.DescriptorData = new byte[] { 0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x40, 0x03, 0x04, 0x01, 0x60, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01 };
            break;
          }
        case 0x02: //Configuration descriptor
          {
            e.DescriptorData = new byte[] { 0x09, 0x02, 0x20, 0x00, 0x01, 0x01, 0x00, 0xA0, 0x32, 0x09, 0x04, 0x00, 0x00, 0x02, 0xFF, 0xFF, 0xFF, 0x00,
									0x07, 0x05, 0x83, 0x03, 0x0A, 0x00, 0x01, 0x07, 0x05, 0x01, 0x02, 0x40, 0x00, 0x00 };

            break;
          }
        default:
          {
            //Uh?
            break;
          }
      }
    }

    public void OnControlRequestReceived(ControlRequestEventArgs e)
    {
      switch (e.bmRequestType)
      {
        case 0x40:
          {
            switch (e.bRequest)
            {
              case 0x00: //reset
              case 0x01: //set modem control
              case 0x02: //set flow control
              case 0x03: //set baud rate
              case 0x04: //set data characteristics
                {
                  e.Ignore = false;
                  break;
                }
              default:
                break;
            }
            break;
          }
        case 0xC0:
          {
            switch (e.bRequest)
            {
              case 0x05: //get status
              case 0x90: //don't know
                {
                  e.ReturnData = new byte[e.wLength];
                  e.Ignore = false;
                  break;
                }
              default:
                  break;
            }

            e.Ignore = false;
            break;
          }
        default:
          break;
      }
    }

    public void OnIncomingDataReceived(IncomingDataEventArgs e)
    {
      Console.WriteLine("Data received: " + BitConverter.ToString(e.Data));
    }

    public void OnShutdown()
    {
      //Do nothing
    }
  }
}
