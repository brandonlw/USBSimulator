using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using USBSimulator;
using USBSimulator.Devices;

namespace Driver
{
  public class Startup
  {
    private static USBSimulatorDevice _device = null;

    [STAThread]
    static void Main()
    {
      Logger.Level = Logger.LoggingLevel.Verbose;
      Logger.LogEntryAdded += Logger_LogEntryAdded;

      _device = new USBSimulatorDevice();

      if (_device != null)
      {
        Console.WriteLine("Specify device to simulate:");
        Console.WriteLine("\t1: Mass storage device");
        Console.WriteLine("\t2: HID keyboard");
        Console.WriteLine("\t3: Forwarder");
        Console.WriteLine("\t4: Serial adapter");
        Console.WriteLine();

        bool done = false;
        while (!done)
        {
          done = true;
          switch (Console.ReadKey(true).Key)
          {
            case ConsoleKey.D1:
              _device.CurrentDevice = new MassStorageDevice(0xAAAA, 0xABCD, "test.img", 65536,
                "Brandon", "Virtual Drive", 1.00);
              break;
            case ConsoleKey.D2:
              _device.CurrentDevice = new HIDKeyboard(0xDEAD, 0xBEEF);
              break;
            case ConsoleKey.D3:
              _device.CurrentDevice = new DeviceForwarder(0x0451, 0xE004);
              break;
            case ConsoleKey.D4:
              _device.CurrentDevice = new SerialAdapter();
              break;
            default:
              done = false;
              break;
          }
        }

        Console.WriteLine("Keys:");
        Console.WriteLine("\tF1: Attach device to host.");
        Console.WriteLine("\tF2: Detach device from host.");
        Console.WriteLine("\tF3: Reset interface (in case simulator was unplugged).");
        if (_device.CurrentDevice as HIDKeyboard != null)
          Console.WriteLine("\tS:  Send sample keypress data.");
        Console.WriteLine("Press [ESC] to disconnect and quit.");

        while (true)
        {
          if (Console.KeyAvailable)
          {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Escape)
              break;
            else if (key == ConsoleKey.S)
            {
              var d = _device.CurrentDevice as HIDKeyboard;

              if (d != null)
              {
                d.ClearKeypresses();
                d.ClearKeypresses();
                d.ClearKeypresses();
                d.SendKey(HIDKeyboard.KeyboardModifier.LeftGUI, HIDKeyboard.KeyboardKey.LetterR);
                d.SendString("notepad");
                d.SendKey(HIDKeyboard.KeyboardKey.Enter);
                d.SendString("This is a test of the emergency broadcast system.");
                d.SendKey(HIDKeyboard.KeyboardKey.Enter);
                d.SendKey(HIDKeyboard.KeyboardKey.Enter);
                d.SendString("Haha, I own you!");
                d.SendKey(HIDKeyboard.KeyboardKey.Enter);
                d.SendKey(HIDKeyboard.KeyboardModifier.LeftGUI, HIDKeyboard.KeyboardKey.LetterR);
                d.SendString("cmd");
                d.SendKey(HIDKeyboard.KeyboardKey.Enter);
              }
            }
            else if (key == ConsoleKey.F1)
            {
              _device.AttachDevice();
            }
            else if (key == ConsoleKey.F2)
            {
              _device.DetachDevice();
            }
            else if (key == ConsoleKey.F3)
            {
              _device.Reset();
            }
          }

          Thread.Sleep(10);
        }

        _device.DetachDevice();
        Thread.Sleep(1000); //HACK: Give time to detach properly
        _device.Close();
      }
    }

    static void Logger_LogEntryAdded(object sender, LogEventArgs e)
    {
      Console.WriteLine(e.Message);
    }
  }
}
