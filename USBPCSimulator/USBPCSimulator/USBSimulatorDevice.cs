using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.DeviceNotify;
using LibUsbDotNet.Main;

namespace USBSimulator
{
  public class USBSimulatorDevice
  {
    private const int _VENDOR_ID = 0xAAAA;
    private const int _PRODUCT_ID = 0xBBBB;

    private IUSBDevice _currentDevice;
    private IUsbDevice _device;
    private UsbEndpointReader _reader;
    private UsbEndpointWriter _writer;
    private Mutex _writerLock;
    private Thread _messageThread;
    private List<Message> _messages;

    public IUSBDevice CurrentDevice
    {
      get
      {
        return _currentDevice;
      }
      set
      {
        _currentDevice = value;

        if (_currentDevice != null)
          _currentDevice.OnInitialize(this);
      }
    }

    private enum MessageStatus
    {
      ReadyToSend,
      PendingResponse
    }

    private class Message
    {
      public MessageStatus Status { get; set; }
      public byte[] Data { get; set; }

      public Message(MessageStatus status, byte[] data)
      {
        Status = status;
        Data = data;
      }
    };

    public USBSimulatorDevice()
    {
      _messages = new List<Message>();
      _writerLock = new Mutex();

      if (!Reset())
        throw new InvalidOperationException("Device failed to initialize successfully");
    }

    public bool Reset()
    {
      bool ret = true;

      Close();

      //Start message thread
      _messageThread = new Thread(new ThreadStart(_SendMessages));
      _messageThread.IsBackground = true;
      _messageThread.Start();

      //Find device
      _device = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(_VENDOR_ID, _PRODUCT_ID)) as IUsbDevice;

      if (_device != null)
      {
        //Set up the incoming and outgoing endpoints

        _reader = _device.OpenEndpointReader(LibUsbDotNet.Main.ReadEndpointID.Ep03);
        _reader.DataReceived += _reader_DataReceived;
        _reader.DataReceivedEnabled = true;

        lock (_writerLock)
        {
          _writer = _device.OpenEndpointWriter(LibUsbDotNet.Main.WriteEndpointID.Ep02);
        }
      }
      else
        ret = false;

      return ret;
    }

    //Tell the device to allow connecting/enumerating with the host.
    public void AttachDevice()
    {
      var buffer = new byte[2];
      buffer[0] = (byte)'S';
      buffer[1] = 0x01;

      _Write(buffer);
    }

    //Tell the device to disconnect (if connected) and prevent connecting/enumerating with the host.
    public void DetachDevice()
    {
      var buffer = new byte[2];
      buffer[0] = (byte)'S';
      buffer[1] = 0x00;

      _Write(buffer);
    }

    //Send raw data on the specified endpoint.
    public void WriteOutgoingData(int endpoint, byte[] data)
    {
      WriteOutgoingData(endpoint, data, 0, data.Length);
    }

    //Send raw data on the specified endpoint.
    public void WriteOutgoingData(int endpoint, byte[] data, int offset, int count)
    {
      var buffer = new byte[2 + count];
      buffer[0] = (byte)'O';
      buffer[1] = (byte)endpoint;
      Array.Copy(data, offset, buffer, 2, count);

      _Write(buffer);
    }

    public void Close()
    {
      if (_messageThread != null)
      {
        try { _messageThread.Abort(); }
        catch { /* Don't care...*/ };

        _messageThread = null;
      }

      try
      {
        if (_device != null && _device.IsOpen)
          _device.Close();
      }
      catch
      {
        //Don't care...
      }
    }

    private void _Write(byte[] message)
    {
      var buffer = new byte[message.Length + 2];

      buffer[0] = (byte)(message.Length & 0xFF);
      buffer[1] = (byte)((message.Length >> 8) & 0xFF);
      Array.Copy(message, 0, buffer, 2, message.Length);

      lock (_messages)
      {
        _messages.Add(new Message(MessageStatus.ReadyToSend, buffer));
      }
    }

    private void _SendMessages()
    {
      while (true)
      {
        try
        {
          lock (_messages)
          {
            for (int i = 0; i < _messages.Count; i++)
            {
              if (_messages[i].Status == MessageStatus.ReadyToSend)
              {
                int transferred;
                _messages[i].Status = MessageStatus.PendingResponse;

                lock (_writerLock)
                {
                  try
                  {
                    if (_writer != null)
                      _writer.Write(_messages[i].Data, 0, _messages[i].Data.Length, 10000, out transferred);
                  }
                  catch
                  {
                    //Oh well...
                  }
                }
                break;
              }
            }
          }
        }
        catch
        {
          //Don't care...
        }

        Thread.Sleep(0);
      }
    }

    private void _reader_DataReceived(object sender, EndpointDataEventArgs e)
    {
      char cmd = (char)e.Buffer[2];

      switch (cmd)
      {
        case 'E':
          {
            Logger.WriteLine("Raw: Received error/busy response: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.VeryVerbose);

            //Need to find the first (oldest) message matching this command and mark it as ready to send
            //HACK: This really should use a sequence ID or something, but this works well enough
            lock (_messages)
            {
              for (int i = 0; i < _messages.Count; i++)
              {
                if (_messages[i].Data[2] == e.Buffer[4])
                {
                  _messages[i].Status = MessageStatus.ReadyToSend;
                  break;
                }
              }
            }

            break;
          }
        case 'A':
          {
            Logger.WriteLine("Raw: Received acknowledgement response: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.VeryVerbose);

            //Need to find the first (oldest) message matching this command and delete it
            //HACK: This really should use a sequence ID or something, but this works well enough
            //  There's also the potential for this to fill up all available memory...don't care for now...
            lock (_messages)
            {
              for (int i = 0; i < _messages.Count; i++)
              {
                if (_messages[i].Data[2] == e.Buffer[3])
                {
                  _messages.RemoveAt(i);
                  break;
                }
              }
            }

            break;
          }
        case 'U':
          {
            Logger.WriteLine("Raw: Received control request: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.Verbose);

            //Received control request
            int bmRequestType = e.Buffer[3];
            int bRequest = e.Buffer[4];
            int wValue = e.Buffer[5] | (e.Buffer[6] << 8);
            int wIndex = e.Buffer[7] | (e.Buffer[8] << 8);
            int wLength = e.Buffer[9] | (e.Buffer[10] << 8);
            var attachedData = new byte[wLength];
            Array.Copy(e.Buffer, 11, attachedData, 0, wLength);

            var arg = new ControlRequestEventArgs(bmRequestType, bRequest, wValue, wIndex, wLength, attachedData);

            _currentDevice.OnControlRequestReceived(arg);

            if (arg.CanIgnore && arg.Ignore)
            {
              _Write(new byte[] { (byte)'U', 0x00 });
            }
            else if (arg.Stall)
            {
              _Write(new byte[] { (byte)'U', 0x02 });
            }
            else
            {
              var ret = new byte[2 + (arg.ReturnData != null ? arg.ReturnData.Length : 0)];

              ret[0] = (byte)'U';
              ret[1] = 0x01;
              if (arg.ReturnData != null)
                Array.Copy(arg.ReturnData, 0, ret, 2, arg.ReturnData.Length);

              _Write(ret);
            }

            break;
          }
        case 'F':
          {
            //If e.Buffer[3] is non-zero, USB is connected, otherwise disconnected. Apparently.
            Logger.WriteLine("Raw: Received event: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.Verbose);

            break;
          }
        case 'D':
          {
            Logger.WriteLine("Raw: Received descriptor request: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.Verbose);

            //Received descriptor request
            int wValue = e.Buffer[3] | (e.Buffer[4] << 8);
            int wIndex = e.Buffer[5] | (e.Buffer[6] << 8);

            var arg = new DescriptorRequestedEventArgs(wValue, wIndex);

            _currentDevice.OnDescriptorRequested(arg);

            //HACK: We tacked on the endpoint configuration stuff to the device descriptor,
            //  so if that's what we're returning, generate and tack it on here
            byte[] ret;
            if (((wValue >> 8) & 0xFF) == 0x01)
            {
              ret = new byte[1 + arg.DescriptorData.Length + 1 + (_currentDevice.Endpoints.Count * 4)];
              ret[0] = (byte)'D';
              Array.Copy(arg.DescriptorData, 0, ret, 1, arg.DescriptorData.Length);

              ret[1 + arg.DescriptorData.Length] = (byte)_currentDevice.Endpoints.Count;
              for (int i = 0; i < _currentDevice.Endpoints.Count; i++)
              {
                ret[1 + arg.DescriptorData.Length + 1 + (i * 4) + 0] =
                  (byte)(_currentDevice.Endpoints[i].Endpoint | (byte)_currentDevice.Endpoints[i].Direction);
                ret[1 + arg.DescriptorData.Length + 1 + (i * 4) + 1] = (byte)_currentDevice.Endpoints[i].Type;
                ret[1 + arg.DescriptorData.Length + 1 + (i * 4) + 2] = (byte)(_currentDevice.Endpoints[i].MaximumPacketSize & 0xFF);
                ret[1 + arg.DescriptorData.Length + 1 + (i * 4) + 3] = (byte)((_currentDevice.Endpoints[i].MaximumPacketSize >> 8) & 0xFF);
              }
            }
            else
            {
              ret = new byte[1 + (arg.DescriptorData != null ? arg.DescriptorData.Length : 0)];
              ret[0] = (byte)'D';
              if (arg.DescriptorData != null)
                Array.Copy(arg.DescriptorData, 0, ret, 1, arg.DescriptorData.Length);
            }

            _Write(ret);

            break;
          }
        case 'I':
          {
            Logger.WriteLine("Raw: Received incoming data: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.Verbose);

            //Incoming endpoint data received
            int count = (e.Buffer[0] | (e.Buffer[1] << 8));
            if (count > 0)
            {
              var data = new byte[count - 2];
              Array.Copy(e.Buffer, 4, data, 0, data.Length);
              _currentDevice.OnIncomingDataReceived(new IncomingDataEventArgs(e.Buffer[3], data));
            }

            break;
          }
        default:
          {
            Logger.WriteLine("Raw: Received unknown data: " +
              BitConverter.ToString(e.Buffer, 0, e.Count), Logger.LoggingLevel.Verbose);
            break;
          }
      }
    }
  }
}
