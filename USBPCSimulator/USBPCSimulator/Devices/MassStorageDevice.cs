using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  //This code makes quite a few assumptions about how commands are logically broken up
  //  on the bulk endpoints, but it's probably okay.
  public class MassStorageDevice : IUSBDevice
  {
    private USBSimulatorDevice _device;
    public List<EndpointInformation> Endpoints { get; internal set; }

    private int _vendorId;
    private int _productId;
    private string _vendorName;
    private string _productName;
    private double _revision;
    private FileStream _image = null;
    private int _sectors;
    private int _dataRemaining;

    public MassStorageDevice(int vendorId, int productId, string imageFileName, int sectors,
      string vendorName, string productName, double revision)
    {
      _vendorId = vendorId;
      _productId = productId;
      _vendorName = vendorName.Substring(0, Math.Min(vendorName.Length, 8));
      _productName = productName.Substring(0, Math.Min(productName.Length, 16));
      _revision = revision;
      _sectors = sectors;

      if (!File.Exists(imageFileName))
      {
        //Create a blank image of at least this size
        _image = new FileStream(imageFileName, FileMode.OpenOrCreate);
        var blank = new byte[0x200];
        for (int i = 0; i < sectors; i++)
          _image.Write(blank, 0, 0x200);
        _image.Close();
      }

      //Open up the image file
      _image = new FileStream(imageFileName, FileMode.OpenOrCreate);
    }

    public void OnInitialize(USBSimulatorDevice device)
    {
      _device = device;

      //Set up the endpoints
      Endpoints = new List<EndpointInformation>();
      Endpoints.Add(new EndpointInformation(0x01,
        EndpointInformation.EndpointDirection.Incoming, EndpointInformation.EndpointType.Bulk, 0x40));
      Endpoints.Add(new EndpointInformation(0x02,
        EndpointInformation.EndpointDirection.Outgoing, EndpointInformation.EndpointType.Bulk, 0x40));
    }

    public void OnControlRequestReceived(ControlRequestEventArgs e)
    {
      if (e.bmRequestType == 0xA1 && e.bRequest == 0xFE)
      {
        //Return max LUN
        e.ReturnData = new byte[] { 0x00 };
        e.Ignore = false;
      }
    }

    public void OnDescriptorRequested(DescriptorRequestedEventArgs e)
    {
      switch ((e.wValue >> 8) & 0xFF)
      {
        case 0x01: //Device descriptor
          {
            e.DescriptorData = new byte[] { 0x12, 0x01, 0x00, 0x02, 0x08, 0x06, 0x50, 0x40,
              (byte)(_vendorId & 0xFF), (byte)((_vendorId >> 8) & 0xFF),
              (byte)(_productId & 0xFF), (byte)((_productId >> 8) & 0xFF),
              0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
            break;
          }
        case 0x02: //Configuration descriptor
          {
            e.DescriptorData = new byte[] { 0x09, 0x02, 0x20, 0x00, 0x01, 0x01, 0x00, 0xE0, 0xFA, 0x09, 0x04, 0x00, 0x00, 0x02, 0x08, 0x06, 0x50, 0x00,
									0x07, 0x05, 0x81, 0x02, 0x40, 0x00, 0x00, 0x07, 0x05, 0x02, 0x02, 0x40, 0x00, 0x00 };
            break;
          }
        default:
          {
            //Uh?
            break;
          }
      }
    }

    public void OnIncomingDataReceived(IncomingDataEventArgs e)
    {
      if (_dataRemaining > 0)
      {
        //This is more write sector data
        _image.Write(e.Data, 0, e.Data.Length);
        _dataRemaining -= e.Data.Length;
      }
      else
      {
        //This is probably a mass storage command
        switch (e.Data[0x0F])
        {
          case 0x00: //Test Unit Ready
            {
              //Just return success
              _SendMassStorageResponse(e.Data, null);
              break;
            }
          case 0x12: //Inquiry
            {
              //Send inquiry data
              var data = new byte[0x24];
              data[0] = 0x00;
              data[1] = 0x00;
              data[3] = 0x01;
              data[4] = 0x1F;
              Array.Copy(ASCIIEncoding.ASCII.GetBytes(_vendorName), 0, data, 8, _vendorName.Length);
              Array.Copy(ASCIIEncoding.ASCII.GetBytes(_productName), 0, data, 16, _productName.Length);
              var revision = _revision.ToString("N2"); revision = revision.Substring(0, Math.Min(revision.Length, 4));
              Array.Copy(ASCIIEncoding.ASCII.GetBytes(revision), 0, data, 32, revision.Length);
              _SendMassStorageResponse(e.Data, data);
              break;
            }
          case 0x1A: //Actually...I'm not sure what this is.
            {
              //Send response
              var data = new byte[8];
              data[0] = 0x12;
              data[7] = 0x1C;
              _SendMassStorageResponse(e.Data, data);
              break;
            }
          case 0x1B: //Start/Stop Unit
            {
              //Just return success
              _SendMassStorageResponse(e.Data, null);
              break;
            }
          case 0x1E: //Prevent/Allow Medium Removal
            {
              //Just return success
              _SendMassStorageResponse(e.Data, null);
              break;
            }
          case 0x23: //Read Format Capacities
            {
              //Send response
              var data = new byte[12];
              data[3] = 0x08;
              data[4] = (byte)(((_sectors - 1) >> 24) & 0xFF);
              data[5] = (byte)(((_sectors - 1) >> 16) & 0xFF);
              data[6] = (byte)(((_sectors - 1) >> 8) & 0xFF);
              data[7] = (byte)((_sectors - 1) & 0xFF);
              data[8] = 0x02;
              data[10] = 0x02;
              _SendMassStorageResponse(e.Data, data);
              break;
            }
          case 0x25: //Read Capacity
            {
              //Send response
              var data = new byte[8];
              data[0] = (byte)(((_sectors - 1) >> 24) & 0xFF);
              data[1] = (byte)(((_sectors - 1) >> 16) & 0xFF);
              data[2] = (byte)(((_sectors - 1) >> 8) & 0xFF);
              data[3] = (byte)((_sectors - 1) & 0xFF);
              data[6] = 0x02;
              _SendMassStorageResponse(e.Data, data);
              break;
            }
          case 0x28: //Read Sector
            {
              ulong LBA = (ulong)(e.Data[0x0F + 3]) | (ulong)(((ulong)e.Data[0x0F + 2] << 8) * 0x10000);
              LBA += (ulong)((ulong)e.Data[0x0F + 5] | (ulong)((ulong)e.Data[0x0F + 4] << 8));
              int sectors = (e.Data[0x0F + 8] & 0xFF) | (e.Data[0x0F + 7] << 8);
              var sectorData = new byte[sectors * 0x200];

              Logger.WriteLine(String.Format("MSD: Read: LBA {0}, Sectors {1}", LBA.ToString("X08"), sectors),
                Logger.LoggingLevel.Verbose);

              //Send response
              _image.Seek((long)(LBA * 0x200), SeekOrigin.Begin);
              for (int i = 0; i < sectors; i++)
                _image.Read(sectorData, 0x200 * i, 0x200);
              _SendMassStorageResponse(e.Data, sectorData);
              break;
            }
          case 0x2A: //Write Sector
            {
              ulong LBA = (ulong)(e.Data[0x0F+3]) | (ulong)(((ulong)e.Data[0x0F+2] << 8) * 0x10000);
              LBA += (ulong)((ulong)e.Data[0x0F+5] | (ulong)((ulong)e.Data[0x0F+4] << 8));
              int sectors = (e.Data[0x0F+8] & 0xFF) | (e.Data[0x0F+7] << 8);

              Logger.WriteLine(String.Format("MSD: Write: LBA {0}, Sectors {1}", LBA.ToString("X08"), sectors),
                Logger.LoggingLevel.Verbose);

              //Seek to the correct point and start writing data as we receive it
              _dataRemaining = sectors * 0x200;
              _image.Seek((long)(LBA * 0x200), SeekOrigin.Begin);
              _SendMassStorageResponse(e.Data, null);
              break;
            }
          case 0x5A: //Mode Sense
            {
              //The host isn't happy until I send this -- not sure what the deal is (I ripped this from a real drive)
              var data = new byte[8];
              data[1] = 0x46;
              data[2] = 0x94;
              _SendMassStorageResponse(e.Data, data);
              break;
            }
          default:
            {
              Logger.WriteLine("MSD: Unknown: " + BitConverter.ToString(e.Data, 0, e.Data.Length),
                Logger.LoggingLevel.Verbose);
              break;
            }
        }
      }
    }

    private void _SendMassStorageResponse(byte[] request, byte[] outgoingData)
    {
      _SendMassStorageResponse(request, outgoingData, 0x00);
    }

    private void _SendMassStorageResponse(byte[] request, byte[] outgoingData, byte status)
    {
      //Build data packet, if we have one
      if (outgoingData != null && outgoingData.Length > 0)
      {
        //Split this up into 0x200 byte packets
        int bytesLeft = outgoingData.Length;
        int bytesSoFar = 0;
        while (bytesLeft > 0)
        {
          int count = Math.Min(bytesLeft, 0x200);
          _device.WriteOutgoingData(0x01, outgoingData, bytesSoFar, count);
          bytesSoFar += count;
          bytesLeft -= count;
        }
      }

      //Send CSW
      var response = new byte[13];
      response[0] = (byte)'U';
      response[1] = (byte)'S';
      response[2] = (byte)'B';
      response[3] = (byte)'S';
      response[4] = request[4];
      response[5] = request[5];
      response[6] = request[6];
      response[7] = request[7];
      _device.WriteOutgoingData(0x01, response);
    }
  }
}
