USBSimulator
============

This is a collection of Teensy 2.0 (C) and PC (C#) files allowing on-the-fly simulation of USB peripherals from a PC.

This requires connecting two Teensy 2.0 USB development boards together by soldering the TX, RX, and GND pins together (TX on one to RX on the other and vice versa, and GND to GND).

One Teensy will connect to a PC that will control the USB connection (we'll call that the slave), and the other Teensy will connect to the USB host you want to "fool" into thinking a specific USB peripheral is attached (we'll call that the master). So:

PC/Slave <-> Teensy #1 <-> Teensy #2 <-> USB Host/Fool

This repository consists of:

api_interface:  This is what you flash to Teensy #1. It simply forwards data between the PC/Slave and Teensy #2.

simulator:      This is what you flash to Teensy #2. It simulates the USB peripheral you specify from PC/Slave.

USBPCSimulator: This is what you run on PC/Slave. It contains all code and logic for manipulating the USB connection.

To build this:
After installing WinAVR (used to build Teensy source), go into the api_interface and simulator directories and run "make." Then flash using Teensy Loader to both Teensy's.
Open USBPCSimulator in Visual Studio C# 2010 Express (or equivalent) and build the solution.

To use:

Install LibUsbDotNet.

Plug two USB A<->mini-B cables into Teensy #1 and Teensy #2, respectively.

Plug the Teensy #1 USB cable into PC/Slave. It will show up as an unknown device with vendor ID 0xAAAA and product ID 0xBBBB. Run the LibUsbDotNet USB InfWizard to generate a driver for this device, and install it.

Plug the Teensy #2 USB cable into the other USB host (AKA fool).

Start USBPCSimulator on PC/Slave, select a device to simulate, and press F1 to establish the connection to the other host.

The other host will suddenly see a new device and install the driver for it. To kill the connection, unplug the USB cable from the other host or press F2 on PC/Slave.

If the connection becomes messed up for any reason, unplug the USB cables on BOTH ENDS (to ensure neither Teensy is hooked up to any source of power), wait a few moments, then plug them back in (Teensy #1 first), and press F3 to redetect Teensy #1.
