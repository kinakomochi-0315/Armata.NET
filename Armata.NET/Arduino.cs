using System.IO.Ports;

namespace Armata.NET;

public enum PinMode
{
    Input = 0x00,  // 入力
    Output = 0x01, // 出力
    Analog = 0x02, // アナログ入力
    PwmOut = 0x03, // PWM出力
    Servo = 0x04,  // サーボモータ
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

    private void SetDigitalState(int port, (int, int) state)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(port, DIGITAL_PORT_COUNT);

        // 0~7ビット目を読み取る
        for (var i = 0; i < 7; i++)
        {
            var idx = port * 8 + i;
            var value = state.Item1 >> i & 1;
            _digitalStates[idx] = (PinState)value;
        }

        // 8ビット目を読み取る
        _digitalStates[(port + 1) * 8 - 1] = (PinState)(state.Item2 & 1);
    }

    private void SetAnalogState(int pin, (int, int) state)
    {
        var value = state.Item2 << 7 | state.Item1;
        _analogStates[pin] = value;
    }

    private (int command, int[] data) ReadSysexCommand()
    {
        var sysexCommand = _serialPort.ReadByte();

        // sysExコマンドの最後(0xF7)まで読み取る
        var sysexData = new List<int>();

        while (true)
        {
            var data = _serialPort.ReadByte();
            if (data == 0xF7) break;
            sysexData.Add(data);
        }

        return (sysexCommand, sysexData.ToArray());
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

            if (command == 0xF0) // sysexコマンド
            {
                var (sysexCommand, sysexData) = ReadSysexCommand();

                // TODO: sysexコマンドの処理

                continue;
            }

            switch (command)
            {
                case >= 0x90 and <= 0x9F: // デジタルIOメッセージ
                    var pinStates = ReadCommand(2).ToArray();
                    Console.WriteLine($"デジタル入力: ${pinStates[0]:X}, ${pinStates[1]:X}");
                    SetDigitalState(command - 0x90, (pinStates[0], pinStates[1]));
                    break;
                case >= 0xE0 and <= 0xEF: // アナログIOメッセージ
                    var analogValue = ReadCommand(2).ToArray();
                    Console.WriteLine($"アナログ入力: ${analogValue[0]:X}, ${analogValue[1]:X}");
                    SetAnalogState(command - 0xA0, (analogValue[0], analogValue[1]));
                    break;
            }
        }

        Console.WriteLine("Disconnected from Arduino.");
    }
}