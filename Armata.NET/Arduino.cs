using System.IO.Ports;

namespace Armata.NET;

public enum PinMode
{
    Input = 0x00,  // 入力
    Output = 0x01, // 出力
    Analog = 0x02, // アナログ入力
    PwmOut = 0x03, // PWM出力
}

public class Arduino(string portName, int baudRate = 57600)
{
    private const int DIGITAL_PORT_COUNT = 8;
    private const int ANALOG_PIN_COUNT = 16;
    private readonly int[] _analogStates = new int[ANALOG_PIN_COUNT];
    private readonly PinState[] _digitalStates = new PinState[DIGITAL_PORT_COUNT * 8];

    private readonly SerialPort _serialPort = new(portName, baudRate);

    public bool IsConnected => _serialPort.IsOpen;

    private static byte GetPort(byte pin)
    {
        return (byte)(pin / 8);
    }

    public void Connect()
    {
        _serialPort.Open();
        Task.Run(SerialReceivingTask);
    }

    public void Disconnect()
    {
        CheckSerialPortIsValid();

        _serialPort.Close();
        _serialPort.Dispose();
    }

    public void PinMode(byte pin, PinMode mode)
    {
        if (pin >= DIGITAL_PORT_COUNT * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        CheckSerialPortIsValid();

        // ピンのモードを設定
        var modeReq = new byte[] { 0xF4, pin, (byte)mode };
        _serialPort.Write(modeReq, 0, modeReq.Length);

        // そのピンの状態が変更されたときに通知するようリクエスト
        switch (mode)
        {
            case NET.PinMode.Input:
                var digitalNotifyReq = new byte[] { (byte)(0xD0 + GetPort(pin)), 1 };
                _serialPort.Write(digitalNotifyReq, 0, digitalNotifyReq.Length);
                break;
            case NET.PinMode.Analog:
                var analogNotifyReq = new byte[] { (byte)(0xC0 + pin), 1 };
                _serialPort.Write(analogNotifyReq, 0, analogNotifyReq.Length);
                break;
        }
    }

    public void DigitalWrite(byte pin, PinState state)
    {
        if (pin >= DIGITAL_PORT_COUNT * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        CheckSerialPortIsValid();

        var bytes = new byte[] { 0xF5, pin, (byte)state };
        _serialPort.Write(bytes, 0, bytes.Length);
    }

    public PinState DigitalRead(byte pin)
    {
        if (pin >= DIGITAL_PORT_COUNT * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        CheckSerialPortIsValid();

        return _digitalStates[pin];
    }

    public int AnalogRead(byte pin)
    {
        if (pin >= ANALOG_PIN_COUNT)
        {
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        CheckSerialPortIsValid();

        return _analogStates[pin];
    }

    public void AnalogWrite(byte pin, int value)
    {
        if (pin >= DIGITAL_PORT_COUNT * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        if (value < 0) value = 0;

        CheckSerialPortIsValid();

        var bytes = new[] { (byte)(0xE0 + pin), (byte)(value % 127), (byte)(value >> 7) };
        _serialPort.Write(bytes, 0, bytes.Length);
    }

    private void CheckSerialPortIsValid()
    {
        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }
    }

    private void ReadDigitalState(int port)
    {
        var pinStates = ReadCommand(2).ToArray();

        // 0~7ビット目を読み取る
        for (var i = 0; i < 7; i++)
        {
            var idx = port * 8 + i;
            var value = pinStates[0] >> i & 1;
            _digitalStates[idx] = (PinState)value;
        }

        // 8ビット目を読み取る
        _digitalStates[(port + 1) * 8 - 1] = (PinState)(pinStates[1] & 1);
    }

    private void ReadAnalogValue(int pin)
    {
        var state = ReadCommand(2).ToArray();
        var value = state[1] << 7 | state[0];

        _analogStates[pin] = value;
    }

    private IEnumerable<int> ReadSysexArgs()
    {
        while (true)
        {
            var data = _serialPort.ReadByte();

            if (data == 0xF7)
            {
                yield break;
            }

            yield return data;
        }
    }

    private IEnumerable<int> ReadCommand(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return _serialPort.ReadByte();
        }
    }

    private void SerialReceivingTask()
    {
        while (IsConnected)
        {
            var command = _serialPort.ReadByte();

            Console.WriteLine($"Received command: {command:X}");

            switch (command)
            {
                case 0xF0: // SysExコマンド
                {
                    var sysExCommand = _serialPort.ReadByte();
                    var sysExData = ReadSysexArgs().ToArray();

                    continue;
                }
                case >= 0x90 and <= 0x9F: // デジタルピン状態通知
                {
                    ReadDigitalState(command - 0x90);
                    break;
                }
                case >= 0xE0 and <= 0xEF: // アナログピン状態通知
                {
                    ReadAnalogValue(command - 0xA0);
                    break;
                }
            }
        }

        Console.WriteLine("Disconnected from Arduino.");
    }
}