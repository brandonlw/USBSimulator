using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace USBSimulator
{
  public interface IUSBDevice
  {
    //List of EndpointInformation objects specifying the endpoints you want to expose.
    //  You should create and fill this yourself in the OnInitialize method.
    //NOTE: There is a limitation on the Teensy 2.0 that forbids both an
    //  incoming and outgoing endpoint with the same number.
    //  If you can get away with using only one endpoint, only specify
    //  the used one here.
    //NOTE: You MUST add the endpoints to this list in *ascending* order
    //  (that is, 0x01, 0x02, 0x03, etc. -- direction does not matter).
    List<EndpointInformation> Endpoints { get; }

    //Called when first initialized.
    //You should create and fill the Endpoints list above here.
    void OnInitialize(USBSimulatorDevice device);

    //Called when shutting down.
    //You should do any cleanup here.
    void OnShutdown();

    //Called when the host is requesting a descriptor.
    //You can access the wValue and wIndex from the request.
    //It is your responsibility to:
    //  Set the DescriptorData property to a byte array of descriptor data,
    //    if you have anything to return.
    void OnDescriptorRequested(DescriptorRequestedEventArgs e);

    //Called when the host has issued a control request.
    //You can access the following from the control request:
    //  bmRequestType
    //  bRequest
    //  wIndex
    //  wValue
    //  wLength
    //  AttachedData (a byte array of any outgoing data included with request)
    //All requests are passed through to the underlying library and
    //  ignored by default.
    //Descriptor requests will pass through here, but you can ignore them.
    //  They will be passed to OnDescriptorRequested.
    //If the CanIgnore property is set to false, you MUST handle this request.
    //  This will be true when outgoing data is attached.
    //To handle the request:
    //  Set the Ignore property to false.
    //  If returning any attached data, set the ReturnData property to the data.
    void OnControlRequestReceived(ControlRequestEventArgs e);

    //Called when incoming data is received on a non-control endpoint.
    //You can access the endpoint number and the byte array of received data.
    void OnIncomingDataReceived(IncomingDataEventArgs e);
  }
}
