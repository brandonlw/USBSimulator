using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace USBSimulator.Devices
{
  //NOTE: This does NOT WORK!!! I think it may be a problem with libusb and multiple devices,
  //  but who knows, really. It's just very erratic sending/receiving endpoint data with the
  //  real device. Hardware analyzer shows it's coming across, but I just don't get the
  //  libusb events sometimes.
  public class DeviceForwarder : IUSBDevice
  {
    public List<EndpointInformation> Endpoints { get; internal set; }
    private USBSimulatorDevice _device;
    private IUsbDevice _forwardee;
    private Dictionary<int, UsbEndpointReader> _readers;
    private Dictionary<int, UsbEndpointWriter> _writers;

    public DeviceForwarder(int vendorId, int productId)
    {
      _Init(vendorId, productId, 0);
    }

    public DeviceForwarder(int vendorId, int productId, int interfaceNumber)
    {
      _Init(vendorId, productId, interfaceNumber);
    }

    private void _Init(int vendorId, int productId, int interfaceNumber)
    {
      _readers = new Dictionary<int, UsbEndpointReader>();
      _writers = new Dictionary<int, UsbEndpointWriter>();

      _forwardee = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(vendorId, productId)) as IUsbDevice;
      if (_forwardee == null) throw new InvalidOperationException("Device not found");

      _forwardee.ClaimInterface(interfaceNumber);
      _forwardee.SetConfiguration(1);
    }

    public void OnInitialize(USBSimulatorDevice device)
    {
      _device = device;

      //Fill the endpoint list as completely as we can
      Endpoints = new List<EndpointInformation>();
      int transferred;
      var configuration = new byte[255];
      int index = 0;
      if (_forwardee.GetDescriptor(0x02, 0x00, 0x00, configuration, 255, out transferred))
      {
        //NOTE: Only supporting one configuration
        int totalLength = configuration[2] | (configuration[3] << 8);
        while (index < totalLength)
        {
          if (configuration[index + 1] == 0x05)
          {
            //This is an endpoint descriptor
            int endpoint = configuration[index + 2] & 0x7F;

            if ((configuration[index + 2] & 0x80) > 0)
            {
              //Incoming endpoint
              _readers.Add(endpoint, _forwardee.OpenEndpointReader(_GetReadEndpointId(endpoint)));
              _readers[endpoint].DataReceived += DeviceForwarder_DataReceived;
              _readers[endpoint].DataReceivedEnabled = true;
              Endpoints.Add(new EndpointInformation(endpoint, EndpointInformation.EndpointDirection.Incoming,
                (EndpointInformation.EndpointType)configuration[index + 3], configuration[index + 4] | (configuration[index + 5] << 8)));
            }
            else
            {
              //Outgoing endpoint
              _writers.Add(endpoint, _forwardee.OpenEndpointWriter(_GetWriteEndpointId(endpoint)));
              Endpoints.Add(new EndpointInformation(endpoint, EndpointInformation.EndpointDirection.Outgoing,
                (EndpointInformation.EndpointType)configuration[index + 3], configuration[index + 4] | (configuration[index + 5] << 8)));
            }
          }

          index += configuration[index];
        }
      }
    }

    public void OnShutdown()
    {
      if (_forwardee != null)
        _forwardee.Close();
    }

    public void OnIncomingDataReceived(IncomingDataEventArgs e)
    {
      //Send it out to the real device
      if (_writers.ContainsKey(e.Endpoint))
      {
        int transferred;
        _writers[e.Endpoint].Write(e.Data, 0, e.Data.Length, 5000, out transferred);
      }
    }

    public void OnControlRequestReceived(ControlRequestEventArgs e)
    {
      if ((e.bmRequestType == 0x80) && (e.bRequest == 0x06))
      {
        //Descriptor request, let the other event handle it
      }
      else if ((e.bmRequestType == 0x00) && (e.bRequest == 0x05))
      {
        //Let the library handle it, needs it to set the address in the Teensy
      }
      else if ((e.bmRequestType == 0x00) && (e.bRequest == 0x09))
      {
        //Let the library handle it, needs it to configure the endpoints in the Teensy
      }
      else
      {
        //Issue the request to the real device, and return whatever it did
        var setup = new UsbSetupPacket((byte)e.bmRequestType, (byte)e.bRequest,
          (short)e.wValue, (short)e.wIndex, (short)e.wLength);
        int transferred;

        if ((e.bmRequestType & 0x80) > 0)
        {
          var ret = new byte[e.wLength];
          _forwardee.ControlTransfer(ref setup, ret, ret.Length, out transferred);
          e.ReturnData = new byte[transferred];
          Array.Copy(ret, 0, e.ReturnData, 0, e.ReturnData.Length);
        }
        else
        {
          _forwardee.ControlTransfer(ref setup, e.AttachedData, e.AttachedData.Length, out transferred);
        }

        e.Ignore = false;
      }
    }

    public void OnDescriptorRequested(DescriptorRequestedEventArgs e)
    {
      //Issue the request to the real device, and return whatever it did
      var setup = new UsbSetupPacket((byte)0x80, (byte)0x06,
        (short)e.wValue, (short)e.wIndex, 0x0FFF);
      int transferred;

      var ret = new byte[0x0FFF];
      _forwardee.ControlTransfer(ref setup, ret, ret.Length, out transferred);
      e.DescriptorData = new byte[transferred];
      Array.Copy(ret, 0, e.DescriptorData, 0, e.DescriptorData.Length);
    }

    private void DeviceForwarder_DataReceived(object sender, EndpointDataEventArgs e)
    {
      //Send it out to the host
      UsbEndpointReader reader = sender as UsbEndpointReader;

      if (reader != null)
        _device.WriteOutgoingData(reader.EpNum & 0x7F, e.Buffer, 0, e.Count);
    }

    private ReadEndpointID _GetReadEndpointId(int endpoint)
    {
      ReadEndpointID ret;

      if (endpoint == 0x01)
        ret = ReadEndpointID.Ep01;
      else if (endpoint == 0x02)
        ret = ReadEndpointID.Ep02;
      else if (endpoint == 0x03)
        ret = ReadEndpointID.Ep03;
      else if (endpoint == 0x04)
        ret = ReadEndpointID.Ep04;
      else if (endpoint == 0x05)
        ret = ReadEndpointID.Ep05;
      else if (endpoint == 0x06)
        ret = ReadEndpointID.Ep06;
      else if (endpoint == 0x07)
        ret = ReadEndpointID.Ep07;
      else if (endpoint == 0x08)
        ret = ReadEndpointID.Ep08;
      else if (endpoint == 0x09)
        ret = ReadEndpointID.Ep09;
      else if (endpoint == 0x0A)
        ret = ReadEndpointID.Ep10;
      else if (endpoint == 0x0B)
        ret = ReadEndpointID.Ep11;
      else if (endpoint == 0x0C)
        ret = ReadEndpointID.Ep12;
      else if (endpoint == 0x0D)
        ret = ReadEndpointID.Ep13;
      else if (endpoint == 0x0E)
        ret = ReadEndpointID.Ep14;
      else if (endpoint == 0x0F)
        ret = ReadEndpointID.Ep15;
      else
        throw new InvalidOperationException("Invalid endpoint ID");

      return ret;
    }

    private WriteEndpointID _GetWriteEndpointId(int endpoint)
    {
      WriteEndpointID ret;

      if (endpoint == 0x01)
        ret = WriteEndpointID.Ep01;
      else if (endpoint == 0x02)
        ret = WriteEndpointID.Ep02;
      else if (endpoint == 0x03)
        ret = WriteEndpointID.Ep03;
      else if (endpoint == 0x04)
        ret = WriteEndpointID.Ep04;
      else if (endpoint == 0x05)
        ret = WriteEndpointID.Ep05;
      else if (endpoint == 0x06)
        ret = WriteEndpointID.Ep06;
      else if (endpoint == 0x07)
        ret = WriteEndpointID.Ep07;
      else if (endpoint == 0x08)
        ret = WriteEndpointID.Ep08;
      else if (endpoint == 0x09)
        ret = WriteEndpointID.Ep09;
      else if (endpoint == 0x0A)
        ret = WriteEndpointID.Ep10;
      else if (endpoint == 0x0B)
        ret = WriteEndpointID.Ep11;
      else if (endpoint == 0x0C)
        ret = WriteEndpointID.Ep12;
      else if (endpoint == 0x0D)
        ret = WriteEndpointID.Ep13;
      else if (endpoint == 0x0E)
        ret = WriteEndpointID.Ep14;
      else if (endpoint == 0x0F)
        ret = WriteEndpointID.Ep15;
      else
        throw new InvalidOperationException("Invalid endpoint ID");

      return ret;
    }
  }
}
