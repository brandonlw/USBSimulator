using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace USBSimulator.Devices
{
  public class HIDKeyboard : IUSBDevice
  {
    private const int _WAIT_TIME_MS = 50;

    private USBSimulatorDevice _device;
    public List<EndpointInformation> Endpoints { get; internal set; }

    private int _vendorId;
    private int _productId;
    private int _waitTimeMs;
    private bool _configured;
    private Thread _sendThread = null;
    private Queue<byte[]> _messages = null;

    public enum KeyboardModifier
    {
      None = 0x00,
      LeftControl = 0x01,
      LeftShift = 0x02,
      LeftAlt = 0x04,
      LeftGUI = 0x08
    }

    public enum KeyboardKey
    {
      None = 0x00,
      LetterA = 0x04,
      LetterB = 0x05,
      LetterC = 0x06,
      LetterD = 0x07,
      LetterE = 0x08,
      LetterF = 0x09,
      LetterG = 0x0A,
      LetterH = 0x0B,
      LetterI = 0x0C,
      LetterJ = 0x0D,
      LetterK = 0x0E,
      LetterL = 0x0F,
      LetterM = 0x10,
      LetterN = 0x11,
      LetterO = 0x12,
      LetterP = 0x13,
      LetterQ = 0x14,
      LetterR = 0x15,
      LetterS = 0x16,
      LetterT = 0x17,
      LetterU = 0x18,
      LetterV = 0x19,
      LetterW = 0x1A,
      LetterX = 0x1B,
      LetterY = 0x1C,
      LetterZ = 0x1D,
      Number1 = 0x1E,
      Number2 = 0x1F,
      Number3 = 0x20,
      Number4 = 0x21,
      Number5 = 0x22,
      Number6 = 0x23,
      Number7 = 0x24,
      Number8 = 0x25,
      Number9 = 0x26,
      Number0 = 0x27,
      Enter = 0x28,
      Escape = 0x29,
      Backspace = 0x2A,
      Tab = 0x2B,
      Space = 0x2C,
      Underscore = 0x2D,
      Period = 0x37,
      QuestionMark = 0x38,
      RightKey = 0x4F,
      LeftKey = 0x50,
      DownKey = 0x51,
      UpKey = 0x52,
      ContextMenu = 0x65
    }

    public HIDKeyboard(int vendorId, int productId, int waitTimeMs = _WAIT_TIME_MS)
    {
      _vendorId = vendorId;
      _productId = productId;
      _waitTimeMs = waitTimeMs;
    }

    //Clear any keypresses.
    public void ClearKeypresses()
    {
      _SendKeypressData(new byte[8]);
    }

    //Send character string as if typed from a keyboard.
    public void SendString(string s)
    {
      foreach (char c in s)
        SendCharacterKey(c, false);

      ClearKeypresses();
    }

    //Send character as if typed from a keyboard.
    public void SendCharacterKey(char c)
    {
      SendCharacterKey(c, true);
    }

    //Send character as if typed from a keyboard, and optionally hold the key down.
    public void SendCharacterKey(char c, bool sendClear)
    {
      var modifier = KeyboardModifier.None;
      KeyboardKey key = KeyboardKey.None;

      if (char.IsLetter(c))
      {
        //We're dealing with a letter
        if (char.IsUpper(c))
          modifier |= KeyboardModifier.LeftShift;

        switch (char.ToUpper(c))
        {
          case 'A':
            key = KeyboardKey.LetterA;
            break;
          case 'B':
            key = KeyboardKey.LetterB;
            break;
          case 'C':
            key = KeyboardKey.LetterC;
            break;
          case 'D':
            key = KeyboardKey.LetterD;
            break;
          case 'E':
            key = KeyboardKey.LetterE;
            break;
          case 'F':
            key = KeyboardKey.LetterF;
            break;
          case 'G':
            key = KeyboardKey.LetterG;
            break;
          case 'H':
            key = KeyboardKey.LetterH;
            break;
          case 'I':
            key = KeyboardKey.LetterI;
            break;
          case 'J':
            key = KeyboardKey.LetterJ;
            break;
          case 'K':
            key = KeyboardKey.LetterK;
            break;
          case 'L':
            key = KeyboardKey.LetterL;
            break;
          case 'M':
            key = KeyboardKey.LetterM;
            break;
          case 'N':
            key = KeyboardKey.LetterN;
            break;
          case 'O':
            key = KeyboardKey.LetterO;
            break;
          case 'P':
            key = KeyboardKey.LetterP;
            break;
          case 'Q':
            key = KeyboardKey.LetterQ;
            break;
          case 'R':
            key = KeyboardKey.LetterR;
            break;
          case 'S':
            key = KeyboardKey.LetterS;
            break;
          case 'T':
            key = KeyboardKey.LetterT;
            break;
          case 'U':
            key = KeyboardKey.LetterU;
            break;
          case 'V':
            key = KeyboardKey.LetterV;
            break;
          case 'W':
            key = KeyboardKey.LetterW;
            break;
          case 'X':
            key = KeyboardKey.LetterX;
            break;
          case 'Y':
            key = KeyboardKey.LetterY;
            break;
          case 'Z':
            key = KeyboardKey.LetterZ;
            break;
          default:
            //Whatever...
            break;
        }
      }
      else if (char.IsNumber(c))
      {
        switch (c)
        {
          case '1':
            key = KeyboardKey.Number1;
            break;
          case '2':
            key = KeyboardKey.Number2;
            break;
          case '3':
            key = KeyboardKey.Number3;
            break;
          case '4':
            key = KeyboardKey.Number4;
            break;
          case '5':
            key = KeyboardKey.Number5;
            break;
          case '6':
            key = KeyboardKey.Number6;
            break;
          case '7':
            key = KeyboardKey.Number7;
            break;
          case '8':
            key = KeyboardKey.Number8;
            break;
          case '9':
            key = KeyboardKey.Number9;
            break;
          case '0':
            key = KeyboardKey.Number0;
            break;
          default:
            //Whatever...
            break;
        }
      }
      else
      {
        if (c == '\n')
          key = KeyboardKey.Enter;
        else if (c == '\t')
          key = KeyboardKey.Tab;
        else if (c == ' ')
          key = KeyboardKey.Space;
        else if (c == '_')
          key = KeyboardKey.Underscore;
        else if (c == '.')
          key = KeyboardKey.Period;
        else if (c == '!')
        {
          key = KeyboardKey.Number1;
          modifier |= KeyboardModifier.LeftShift;
        }
        else if (c == '?')
        {
          key = KeyboardKey.QuestionMark;
          modifier |= KeyboardModifier.LeftShift;
        }
      }

      SendKey(modifier, key, sendClear);
    }

    //Press the specified modifier key.
    public void SendModifier(KeyboardModifier modifier)
    {
      SendKey(modifier, KeyboardKey.None);
    }

    //Press the specified modifier key, and optionally leave it held down.
    public void SendModifier(KeyboardModifier modifier, bool sendClear)
    {
      SendKey(modifier, KeyboardKey.None, sendClear);
    }

    //Press the specified key.
    public void SendKey(KeyboardKey key)
    {
      SendKey(KeyboardModifier.None, key);
    }

    //Press the specified modifier and key.
    public void SendKey(KeyboardModifier modifier, KeyboardKey key)
    {
      SendKey(modifier, key, true);
    }

    //Press the specified modifier and key, and optionally leave them held down.
    public void SendKey(KeyboardModifier modifier, KeyboardKey key, bool sendClear)
    {
      var data = new byte[8];
      data[0] = (byte)modifier;
      data[2] = (byte)key;
      _SendKeypressData(data);

      if (sendClear)
        ClearKeypresses();
    }

    public void OnInitialize(USBSimulatorDevice device)
    {
      _device = device;
      _messages = new Queue<byte[]>();

      //Start message queueing thread
      _sendThread = new Thread(new ThreadStart(_SendMessages));
      _sendThread.IsBackground = true;
      _sendThread.Start();

      //Build endpoint information
      Endpoints = new List<EndpointInformation>();
      Endpoints.Add(new EndpointInformation(0x01,
        EndpointInformation.EndpointDirection.Incoming, EndpointInformation.EndpointType.Interrupt, 0x08));
    }

    public void OnShutdown()
    {
      //Nothing to do...
    }

    public void OnIncomingDataReceived(IncomingDataEventArgs e)
    {
      //This shouldn't ever happen...
    }

    public void OnDescriptorRequested(DescriptorRequestedEventArgs e)
    {
      switch ((e.wValue >> 8) & 0xFF)
      {
        case 0x01: //Device descriptor
          {
            e.DescriptorData = new byte[] { 0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x40,
              (byte)(_vendorId & 0xFF), (byte)((_vendorId >> 8) & 0xFF),
              (byte)(_productId & 0xFF), (byte)((_productId >> 8) & 0xFF),
              0x01, 0x00, 0x00, 0x00, 0x00, 0x01 };
            break;
          }
        case 0x02: //Configuration descriptor
          {
            e.DescriptorData = new byte[] { 0x09, 0x02, 0x00, 0x00, 0x01, 0x01, 0x00, 0x80, 0x96, 0x09, 0x04, 0x00, 0x00, 0x01, 0x03, 0x01, 0x01, 0x00, 0x09,
            0x21, 0x10, 0x01, 0x00, 0x01, 0x22, 0x40, 0x00, 0x07, 0x05, 0x81, 0x03, 0x08, 0x00, 0x20};

            //Calculate the size
            e.DescriptorData[2] = (byte)(e.DescriptorData.Length & 0xFF);
            e.DescriptorData[3] = (byte)((e.DescriptorData.Length >> 8) & 0xFF);
            
            break;
          }
        case 0x22: //HID report descriptor
          {
            e.DescriptorData = new byte[] { 0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25, 0x01,
            0x75, 0x01, 0x95, 0x08, 0x81, 0x02, 0x95, 0x01, 0x75, 0x08, 0x81, 0x01, 0x95, 0x05, 0x75, 0x01, 0x05, 0x08, 0x19, 0x01, 0x29,
            0x05, 0x91, 0x02, 0x95, 0x01, 0x75, 0x03, 0x91, 0x01, 0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x26, 0xA4, 0x00, 0x05, 0x07, 0x19,
            0x00, 0x29, 0xA4, 0x81, 0x00, 0xC0 };
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
      //Mark some HID requests as handled
      if (e.bmRequestType == 0x21 && e.bRequest == 0x0A)
        e.Ignore = false;
      if (e.bmRequestType == 0x00 && e.bRequest == 0x09)
        _configured = true; //save configured state
    }

    private void _SendKeypressData(byte[] data)
    {
      lock (_messages)
      {
        _messages.Enqueue(data);
      }
    }

    private void _SendMessages()
    {
      while (true)
      {
        try
        {
          if (_configured)
          {
            byte[] msg = null;

            lock (_messages)
            {
              if (_messages.Count > 0)
                msg = _messages.Dequeue();
            }

            if (msg != null)
              _device.WriteOutgoingData(0x01, msg);
          }
        }
        catch
        {
          //Don't care...
        }

        Thread.Sleep(_waitTimeMs);
      }
    }
  }
}
