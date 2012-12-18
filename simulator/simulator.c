#include <avr/io.h>
#include <avr/wdt.h>
#include <avr/power.h>
#include <avr/interrupt.h>
#include <stdbool.h>
#include <string.h>
#include "uart.h"

#include <LUFA/Drivers/USB/USB.h>
#include <LUFA/Drivers/Board/LEDs.h>

#define BAUD_RATE 57600

static volatile uint8_t endpointData[61];
static volatile uint8_t transmitBuffer[1024+2];
static volatile uint8_t receiveBuffer[1024+2];
static volatile bool receivedFirst = false;
static volatile bool packetHandled = false;
static volatile uint16_t bytesWaiting = 0;
static volatile uint16_t bytesSoFar = 0;
static volatile uint16_t bytesLeftToSend = 0;
static volatile uint16_t bytesSentSoFar = 0;
static volatile uint16_t receiveTimeout = 0;

void HandleReceivedPacket(void);

void SetupHardware(void)
{
	/* Disable watchdog if enabled by bootloader/fuses */
	MCUSR &= ~(1 << WDRF);
	wdt_disable();

	/* Disable clock division */
	clock_prescale_set(clock_div_1);

	/* Hardware Initialization */
	LEDs_Init();
	uart_init(BAUD_RATE);
	USB_Init();
	USB_Detach();
}

//Returns true if there's a packet in receiveBuffer ready to parse.
bool SendReceive_IsPacketReady(void)
{
	return ((bytesWaiting == 0) && (bytesSoFar > 0) && !packetHandled);
}

void SendReceive_SetHandled(void)
{
	packetHandled = true;
	bytesSoFar = 0;
	LEDs_SetAllLEDs(LEDS_NO_LEDS);
}

void SendReceive_StartSend(void)
{
	bytesLeftToSend = (transmitBuffer[0] | (transmitBuffer[1] << 8)) + 2;
	bytesSentSoFar = 0;
}

//Returns true if we're still in the middle of sending a packet in transmitBuffer.
bool SendReceive_IsPacketSending(void)
{
	return (bytesLeftToSend > 0);
}

void USBIO_ForwardIncomingData(uint8_t endpoint)
{
	Endpoint_SelectEndpoint(endpoint);
	if (Endpoint_IsReadWriteAllowed() && Endpoint_IsOUTReceived())
	{
		//Wait for anything currently sending to finish first
		while (SendReceive_IsPacketSending())
		{
			SendReceive_Task();
		}

		//Read this data into our buffer
		uint16_t bytesProcessed = 0;
		uint16_t count = Endpoint_BytesInEndpoint();
		uint8_t errorCode;
		transmitBuffer[0] = (count + 2) & 0xFF;
		transmitBuffer[1] = ((count + 2) >> 8) & 0xFF;
		transmitBuffer[2] = 'I';
		transmitBuffer[3] = endpoint;
		while ((errorCode = Endpoint_Read_Stream_LE(transmitBuffer+4, count,
			&bytesProcessed)) == ENDPOINT_RWSTREAM_IncompleteTransfer)
		{
			//Hang on...
		}
		Endpoint_ClearOUT();

		//Write the data to serial UART
		SendReceive_StartSend();
		while (SendReceive_IsPacketSending())
		{
			SendReceive_Task();
		}
	}
}

void USBIO_Task(void)
{
	//Handle incoming data from all appropriate endpoints
	for (int i = 0; i < endpointData[0]; i++)
	{
		if ((endpointData[1+i*4+0] & 0x80) == 0)
		{
			//This is an incoming ("outgoing" from the host's perspective) endpoint
			//Forward its data, if we have any
			USBIO_ForwardIncomingData(endpointData[1+i*4+0]);
		}
	}
}

void SendReceive_Task()
{
	//If we've timed out, start over
	if ((bytesSoFar > 0) && (bytesWaiting > 0))
	{
		if (receiveTimeout++ > 0xFFF0)
		{
			bytesWaiting = 0;
			bytesSoFar = 0;
			receiveTimeout = 0;
			LEDs_SetAllLEDs(LEDS_NO_LEDS);
		}
	}

	//Perform any serial I/O tasks
	if (uart_available() >= 2 && (bytesSoFar == 0) && (!receivedFirst || packetHandled))
	{
		//We have at least the size bytes, start setting things up
		LEDs_SetAllLEDs(LEDS_LED1);
		receiveBuffer[0] = uart_getchar();
		receiveBuffer[1] = uart_getchar();
		bytesWaiting = receiveBuffer[0] | (receiveBuffer[1] << 8);
		bytesSoFar = 2;
		packetHandled = false;
		receiveTimeout = 0;
		receivedFirst = true;
	}
	else if (uart_available())
	{
		//There's data to receive, receive it into our buffer
		while (uart_available() && bytesWaiting)
		{
			receiveBuffer[bytesSoFar++] = uart_getchar();
			bytesWaiting--;
		}
	}
	
	if (bytesLeftToSend > 0)
	{
		//There's data to write, write it out the serial UART
		uart_putchar(transmitBuffer[bytesSentSoFar++]);
		bytesLeftToSend--;
	}
}

void SendReceive_WaitForPacket(uint8_t cmd)
{
	//Actually wait on the data
	while (true)
	{
		while (!SendReceive_IsPacketReady())
		{
			SendReceive_Task();
		}
		SendReceive_SetHandled();
		
		//Now that we have data, see if it's the packet we wanted
		uint8_t cmdReceived = receiveBuffer[2];
		if (cmdReceived != cmd)
		{
			//No, so send a NACK and then wait again

			//Let anything currently sending make it out first
			while (SendReceive_IsPacketSending())
			{
				SendReceive_Task();
			}
			
			//Send the NACK
			transmitBuffer[0] = 0x03;
			transmitBuffer[1] = 0x00;
			transmitBuffer[2] = 'E';
			transmitBuffer[3] = cmdReceived;
			transmitBuffer[4] = cmd;
			SendReceive_StartSend();
			while (SendReceive_IsPacketSending())
			{
				SendReceive_Task();
			}
		}
		else
		{
			//Let anything currently sending make it out first
			while (SendReceive_IsPacketSending())
			{
				SendReceive_Task();
			}
			
			//Send the ACK
			transmitBuffer[0] = 0x03;
			transmitBuffer[1] = 0x00;
			transmitBuffer[2] = 'A';
			transmitBuffer[3] = cmdReceived;
			transmitBuffer[4] = cmd;
			SendReceive_StartSend();
			while (SendReceive_IsPacketSending())
			{
				SendReceive_Task();
			}
			break;
		}
	}
}

void HandleReceivedPacket(void)
{
	if (SendReceive_IsPacketReady())
	{
		//Parse this command
		SendReceive_SetHandled();

		switch (receiveBuffer[2])
		{
			case 'O':
			{
				//We've received data to forward out to USB host on specified endpoint
				//We need to send this data over an endpoint
				Endpoint_SelectEndpoint(receiveBuffer[3]);
				if (Endpoint_IsReadWriteAllowed())
				{
					uint16_t bytesProcessed = 0;
					uint8_t errorCode;
					uint16_t total = receiveBuffer[0] | (receiveBuffer[1] << 8);
					while ((errorCode = Endpoint_Write_Stream_LE(receiveBuffer+4, total - 2,
						&bytesProcessed)) == ENDPOINT_RWSTREAM_IncompleteTransfer)
					{
						//Hang on...
					}
					Endpoint_ClearIN();

					bytesSoFar = 0;
				}

				break;
			}
			case 'S':
			{
				//This is a status command -- we should either attach or detach here
				if (receiveBuffer[3] == 0x01)
				{
					USB_Attach();
				}
				else
				{
					USB_Detach();
				}
				
				break;
			}
			default:
			{
				//Let anything currently sending make it out first
				while (SendReceive_IsPacketSending())
				{
					SendReceive_Task();
				}

				//Echo back "C" along with unknown message ID
				transmitBuffer[0] = 0x02;
				transmitBuffer[1] = 0x00;
				transmitBuffer[2] = 0x43;
				transmitBuffer[3] = receiveBuffer[2];
				SendReceive_StartSend();
				while (SendReceive_IsPacketSending())
				{
					SendReceive_Task();
				}

				break;
			}
		}
		
		//Let anything currently sending make it out first
		while (SendReceive_IsPacketSending())
		{
			SendReceive_Task();
		}
		
		//Send the ACK
		transmitBuffer[0] = 0x02;
		transmitBuffer[1] = 0x00;
		transmitBuffer[2] = 'A';
		transmitBuffer[3] = receiveBuffer[2];
		SendReceive_StartSend();
		LEDs_SetAllLEDs(LEDS_LED1);
		while (SendReceive_IsPacketSending())
		{
			SendReceive_Task();
		}
		LEDs_SetAllLEDs(LEDS_NO_LEDS);
	}
}

int main(void)
{
	SetupHardware();

	sei();

	while (true)
	{
		SendReceive_Task();
		HandleReceivedPacket();
		USB_USBTask();
		USBIO_Task();
	}
}

void EVENT_USB_Device_Connect(void)
{
	//Let anything currently sending make it out first
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}

	//Send "connected" event
	transmitBuffer[0] = 0x02;
	transmitBuffer[1] = 0x00;
	transmitBuffer[2] = 'F';
	transmitBuffer[3] = 0x01;
	SendReceive_StartSend();
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}
}

void EVENT_USB_Device_Disconnect(void)
{
	//Let anything currently sending make it out first
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}

	//Send "disconnected" event
	transmitBuffer[0] = 0x02;
	transmitBuffer[1] = 0x00;
	transmitBuffer[2] = 'F';
	transmitBuffer[3] = 0x00;
	SendReceive_StartSend();
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}
}

void EVENT_USB_Device_ConfigurationChanged(void)
{
	//Configure the endpoints based on the data we snuck in on the device descriptor response
	for (int i = 0; i < endpointData[0]; i++)
	{
		Endpoint_ConfigureEndpoint(endpointData[1+i*4+0],
			endpointData[1+i*4+1], endpointData[1+i*4+2] | (endpointData[1+i*4+3] << 8), 1);
	}
}

void EVENT_USB_Device_ControlRequest(void)
{
	//Send the request to the PC to see what it thinks we should do

	//Let anything currently sending make it out first
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}

	bool handlingCommand = false;
	int total = 9;
	
	//If this is a control request + outgoing data, go ahead and receive the data now to transmitBuffer+11
	if (((USB_ControlRequest.bmRequestType & CONTROL_REQTYPE_DIRECTION) == REQDIR_HOSTTODEVICE) &&
		(USB_ControlRequest.wLength > 0))
	{
		total += USB_ControlRequest.wLength;
		Endpoint_ClearSETUP();
		Endpoint_Read_Control_Stream_LE(transmitBuffer+11, USB_ControlRequest.wLength);
		handlingCommand = true; //Now we have to handle it
	}

	//Send the PC command
	transmitBuffer[0] = total & 0xFF;
	transmitBuffer[1] = (total >> 8) & 0xFF;
	transmitBuffer[2] = 'U';
	transmitBuffer[3] = USB_ControlRequest.bmRequestType;
	transmitBuffer[4] = USB_ControlRequest.bRequest;
	transmitBuffer[5] = USB_ControlRequest.wValue & 0xFF;
	transmitBuffer[6] = (USB_ControlRequest.wValue >> 8) & 0xFF;
	transmitBuffer[7] = USB_ControlRequest.wIndex & 0xFF;
	transmitBuffer[8] = (USB_ControlRequest.wIndex >> 8) & 0xFF;
	transmitBuffer[9] = USB_ControlRequest.wLength & 0xFF;
	transmitBuffer[10] = (USB_ControlRequest.wLength >> 8) & 0xFF;
	SendReceive_StartSend();
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}

	//Wait for response
	SendReceive_WaitForPacket('U');
	
	//Based on the response, either:
	//If we're handling this command:
	bool outgoingRequest = handlingCommand;
	handlingCommand |= ((receiveBuffer[3] & 0x01) > 0); //for outgoing data requests, we don't get a choice -- must handle it
	if (handlingCommand)
	{
		if (!outgoingRequest)
			Endpoint_ClearSETUP();
	//	If data was specified:
		int total = receiveBuffer[0] | (receiveBuffer[1] << 8);
		if (total != 2)
		{
	//		Write it to the control pipe
			Endpoint_Write_Control_Stream_LE(receiveBuffer+4, total - 2);
		}
	//	Clear status stage
		Endpoint_ClearStatusStage();
	}
	else
	{
		//Do nothing at all and hope the underlying library will deal with it appropriately
	}
}

void EVENT_USB_Device_StartOfFrame(void)
{
	//Do nothing
}

uint16_t CALLBACK_USB_GetDescriptor(const uint16_t wValue,
                                    const uint8_t wIndex,
                                    const void** const DescriptorAddress)
{
	const uint8_t  DescriptorType   = (wValue >> 8);
	const uint8_t  DescriptorNumber = (wValue & 0xFF);

	const void* Address = NULL;
	uint16_t    Size    = NO_DESCRIPTOR;
	
	//Let anything currently sending make it out first
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}
	
	//Send the request back to the PC for a descriptor response
	transmitBuffer[0] = 0x05;
	transmitBuffer[1] = 0x00;
	transmitBuffer[2] = 'D';
	transmitBuffer[3] = wValue & 0xFF;
	transmitBuffer[4] = (wValue >> 8) & 0xFF;
	transmitBuffer[5] = wIndex & 0xFF;
	transmitBuffer[6] = (wIndex >> 8) & 0xFF;
	SendReceive_StartSend();
	while (SendReceive_IsPacketSending())
	{
		SendReceive_Task();
	}

	//Wait for response
	SendReceive_WaitForPacket('D');

	switch (DescriptorType)
	{
		case DTYPE_Device:
			Address = receiveBuffer+2+1;
			Size	= receiveBuffer[3];
			//HACK: Piggyback the endpoint configuration data onto the device descriptor
			//Horrible, I know, but for whatever reason LUFA has a problem with me doing this from USB_Device_ConfigurationChanged
			for (int i = 0; i < 61; i++)
				endpointData[i] = receiveBuffer[2+1+Size+i];
			break;
		case DTYPE_Configuration:
			Address	= receiveBuffer+2+1;
			Size	= receiveBuffer[5] | (receiveBuffer[6] << 8);
			break;
		default:
			Address = receiveBuffer+2+1;
			Size	= (receiveBuffer[0] | (receiveBuffer[1] << 8)) - 1;
			break;
	}

	*DescriptorAddress = Address;
	return Size;
}
