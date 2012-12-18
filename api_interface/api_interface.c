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

static volatile PROGMEM uint8_t deviceDescriptor[] = {0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x40, 0xAA, 0xAA, 0xBB, 0xBB, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01};
static volatile PROGMEM uint8_t configDescriptor[] = {0x09, 0x02, 0x27, 0x00, 0x01, 0x01, 0x00, 0xE0, 0xFA, 0x09, 0x04, 0x00, 0x00, 0x03, 0xFF, 0x00, 0x00, 0x00,
									0x07, 0x05, 0x81, 0x03, 0x0A, 0x00, 0x01, 0x07, 0x05, 0x02, 0x02, 0x40, 0x00, 0x00,
									0x07, 0x05, 0x83, 0x02, 0x40, 0x00, 0x00};
static volatile PROGMEM uint8_t stringDescriptor[] = {0x14, 0x00, 'P', 0x00, 'r', 0x00, 'o', 0x00, 'l', 0x00, 'i', 0x00, 'f', 0x00, 'i', 0x00, 'c', 0x00, 0x00, 0x00};
static volatile uint8_t transmitBuffer[1024+2];
static volatile uint8_t receiveBuffer[1024+2];
static volatile uint16_t bytesWaiting = 0;
static volatile uint16_t bytesSoFar = 0;
static volatile uint16_t bytesLeftToSend = 0;
static volatile uint16_t bytesSentSoFar = 0;
static volatile uint16_t receiveTimeout = 0;
static volatile bool packetHandled = true;

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
}

void CheckTimeout(void)
{
	if ((bytesWaiting > 0) && (bytesSoFar > 0))
	{
		if (receiveTimeout++ > 0xFFF0)
		{
			bytesWaiting = 0;
			bytesSoFar = 0;
			receiveTimeout = 0;
			packetHandled = true;
			LEDs_SetAllLEDs(LEDS_NO_LEDS);
		}
	}
}

void SendReceive_Task(void)
{
	//If we've timed out, start over
	CheckTimeout();

	Endpoint_SelectEndpoint(2);
	if (Endpoint_IsReadWriteAllowed() && Endpoint_IsOUTReceived())
	{
		//Read this data into our buffer
		uint16_t bytesProcessed = 0;
		uint8_t errorCode;
		while ((errorCode = Endpoint_Read_Stream_LE(transmitBuffer, 2,
			&bytesProcessed)) == ENDPOINT_RWSTREAM_IncompleteTransfer)
		{
			//Hang on...
			CheckTimeout();
		}
		uint16_t count = transmitBuffer[0] | (transmitBuffer[1] << 8);
		bytesProcessed = 0;
		while ((errorCode = Endpoint_Read_Stream_LE(transmitBuffer+2, count,
			&bytesProcessed)) == ENDPOINT_RWSTREAM_IncompleteTransfer)
		{
			//Hang on...
			CheckTimeout();
		}
		Endpoint_ClearOUT();

		//Write the data to serial UART
		uart_putchar(transmitBuffer[0]);
		uart_putchar(transmitBuffer[1]);
		bytesLeftToSend = count;
		bytesSentSoFar = 2;
		while (bytesLeftToSend > 0)
		{
			uart_putchar(transmitBuffer[bytesSentSoFar++]);
			bytesLeftToSend--;
			CheckTimeout();
		}
	}

	//Perform any serial I/O tasks
	if (uart_available() >= 2 && (bytesSoFar == 0))
	{
		//We have at least the size bytes, start setting things up
		LEDs_SetAllLEDs(LEDS_LED1);
		receiveBuffer[0] = uart_getchar();
		receiveBuffer[1] = uart_getchar();
		bytesWaiting = receiveBuffer[0] | (receiveBuffer[1] << 8);
		bytesSoFar = 2;
		receiveTimeout = 0;
		packetHandled = false;
	}
	
	if (uart_available() && bytesSoFar > 0)
	{
		//There's data to receive, receive it into our buffer
		while (uart_available() && bytesWaiting)
		{
			receiveBuffer[bytesSoFar++] = uart_getchar();
			bytesWaiting--;
			CheckTimeout();
		}
	}
	
	if (bytesLeftToSend > 0)
	{
		//There's data to write, write it out the serial UART
		uart_putchar(transmitBuffer[bytesSentSoFar++]);
		bytesLeftToSend--;
	}

	if ((bytesWaiting == 0) && (bytesSoFar > 0))
	{
		//Send this data over USB
		Endpoint_SelectEndpoint(3);
		if (Endpoint_IsReadWriteAllowed())
		{
			LEDs_SetAllLEDs(LEDS_NO_LEDS);
			uint16_t bytesProcessed = 0;
			uint8_t errorCode;
			uint16_t total = receiveBuffer[0] | (receiveBuffer[1] << 8);
			while ((errorCode = Endpoint_Write_Stream_LE(receiveBuffer, total+2,
				&bytesProcessed)) == ENDPOINT_RWSTREAM_IncompleteTransfer)
			{
				//Hang on...
				CheckTimeout();
			}
			Endpoint_ClearIN();

			bytesSoFar = 0;
			packetHandled = true;
		}
	}
}

int main(void)
{
	SetupHardware();

	sei();

	while (true)
	{
		SendReceive_Task();
		USB_USBTask();
	}
}

void EVENT_USB_Device_Connect(void)
{
	//Do nothing
}

void EVENT_USB_Device_Disconnect(void)
{
	//Do nothing
}

void EVENT_USB_Device_ConfigurationChanged(void)
{
	Endpoint_ConfigureEndpoint(0x81, 0x03, 0x0A, 1);
	Endpoint_ConfigureEndpoint(0x02, 0x02, 0x40, 1);
	Endpoint_ConfigureEndpoint(0x83, 0x02, 0x40, 1);
}

void EVENT_USB_Device_ControlRequest(void)
{
	switch (USB_ControlRequest.bmRequestType)
	{
		case 0x40:
		{
			switch (USB_ControlRequest.bRequest)
			{
				case 0x01: //Vendor Write Value request
				{
					//wValue is the "address"/register
					//wIndex is the value (oddly enough)
					Endpoint_ClearSETUP();
					Endpoint_ClearStatusStage();
					break;
				}
				default:
				{
					break;
				}
			}

			break;
		}
		case 0xC0:
		{
			switch (USB_ControlRequest.bRequest)
			{
				case 0x01: //Vendor Read Value request
				{
					//wValue is the "address"/register
					//wLength should be 1, but whatever
					Endpoint_ClearSETUP();
					Endpoint_Write_Control_Stream_LE(receiveBuffer, USB_ControlRequest.wLength);
					Endpoint_ClearStatusStage();
					break;
				}
				default:
				{
					break;
				}
			}
			
			break;
		}
		default:
		{
			break;
		}
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
	
	switch (DescriptorType)
	{
		case DTYPE_Device:
			Address = &deviceDescriptor;
			Size	= pgm_read_byte(&(deviceDescriptor[0]));
			break;
		case DTYPE_Configuration:
			Address	= &configDescriptor;
			Size	= sizeof(configDescriptor);
			break;
		case DTYPE_String:
			Address	= &stringDescriptor;
			Size	= sizeof(stringDescriptor);
			break;
		default:
			break;
	}

	*DescriptorAddress = Address;
	return Size;
}
