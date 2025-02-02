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
    private const int DIGITAL_PIN_COUNT = DIGITAL_PORT_COUNT * 8;
    private const int ANALOG_PIN_COUNT = 16;

    private const int SYSEX_BEGIN = 0xF0;
    private const int SYSEX_END = 0xF7;
    private const int SYSEX_ANALOG_MAPPING_QUERY = 0x69;
    private const int SYSEX_ANALOG_MAPPING_RESPONSE = 0x6A;
    private const int SYSEX_CAPABILITY_QUERY = 0x6B;
    private const int SYSEX_CAPABILITY_RESPONSE = 0x6C;
    private const int SYSEX_PIN_STATE_QUERY = 0x6D;
    private const int SYSEX_PIN_STATE_RESPONSE = 0x6E;
    private const int SYSEX_EXTENDED_ANALOG = 0x6F;
    private const int SYSEX_SERVO_CONFIG = 0x70;
    private const int SYSEX_STRING_DATA = 0x71;
    private const int SYSEX_SHIFT_DATA = 0x75;
    private const int SYSEX_IIC_REQUEST = 0x76;
    private const int SYSEX_IIC_REPLY = 0x77;
    private const int SYSEX_IIC_CONFIG = 0x78;
    private const int SYSEX_REPORT_FIRMWARE = 0x79;
    private const int SYSEX_SAMPLING_INTERVAL = 0x7A;
    private const int SYSEX_NON_REALTIME = 0x7E;
    private const int SYSEX_REALTIME = 0x7F;

    private const int DIGITAL_MESSAGE = 0x90;
    private const int ANALOG_MESSAGE = 0xE0;

    private readonly int[] _analogStates = new int[ANALOG_PIN_COUNT];
    private readonly PinState[] _digitalStates = new PinState[DIGITAL_PORT_COUNT * 8];
    private readonly SerialPort _serialPort = new(portName, baudRate);

    public bool IsConnected => _serialPort.IsOpen;

    /// <summary>
    /// 利用可能なシリアルポートの名前を取得します。
    /// </summary>
    /// <returns></returns>
    public static string[] GetPortNames() => SerialPort.GetPortNames();

    /// <summary>
    /// Firmataプロトコルにおいてデジタルピンの状態を取得・設定するために使用されるポートという単位に
    /// ピン番号を変換します。ポートは8ビットのデジタルピンをグループ化したものです。
    /// </summary>
    /// <param name="pin">変換するピン番号</param>
    /// <returns>ピン番号が所属するポート番号</returns>
    private static int GetPort(int pin)
    {
        return pin / 8;
    }

    /// <summary>
    /// Firmataプロトコルの7ビットの2つのバイトを結合して1つの整数値に変換します。
    /// </summary>
    /// <param name="low">前ビット</param>
    /// <param name="high">後ビット</param>
    /// <returns></returns>
    private static int ConcatBytes(int low, int high)
    {
        return low | high << 7;
    }

    /// <summary>
    /// 整数値を7ビットの2つのバイトに分割します。
    /// </summary>
    /// <param name="value">分割するビット</param>
    /// <returns>変換後のバイト列(low, high)</returns>
    private static byte[] SplitBytes(int value)
    {
        return [(byte)(value % 127), (byte)(value >> 7)];
    }

    /// <summary>
    /// Arduinoとのシリアル通信を確立します。
    /// </summary>
    public void Connect()
    {
        _serialPort.Open();
        DebugConsole.Info("Arduino接続成功");

        Task.Run(SerialReceivingTask);
        DebugConsole.Info("Arduinoからのデータ受信を開始");
    }

    /// <summary>
    /// Arduinoとのシリアル通信を切断します。
    /// </summary>
    public void Disconnect()
    {
        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        _serialPort.Close();
        _serialPort.Dispose();

        DebugConsole.Info("Arduinoとの接続を切断");
    }

    /// <summary>
    /// ArduinoのGPIOピンのモードを設定します。
    /// </summary>
    /// <param name="pin">設定を変更するピンの番号</param>
    /// <param name="mode">ピンのモード</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void PinMode(int pin, PinMode mode)
    {
        if (pin is not (>= 0 and < DIGITAL_PIN_COUNT))
        {
            // ピン番号がFirmataプロトコルの範囲外のとき
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        // ピンのモードを設定
        Write(0xF4, (byte)pin, (byte)mode);

        switch (mode)
        {
            // 入力系のピンの場合、ピンの状態を通知するように設定
            case NET.PinMode.Input:
            {
                var command = (byte)(0xD0 + GetPort(pin));
                Write(command, 1);
                break;
            }
            case NET.PinMode.Analog:
            {
                var command = (byte)(0xC0 + pin);
                Write(command, 1);
                break;
            }
        }
    }

    /// <summary>
    /// デジタルピンの状態を設定します。
    /// </summary>
    /// <param name="pin">状態を設定するピンの番号</param>
    /// <param name="state">設定する状態</param>
    /// <exception cref="ArgumentOutOfRangeException">Firmata範囲(0~127)外のピン番号を指定したとき</exception>
    /// <exception cref="InvalidOperationException">connect()を呼び出してArduinoに接続していないとき</exception>
    public void DigitalWrite(int pin, PinState state)
    {
        if (pin is not (>= 0 and < DIGITAL_PIN_COUNT))
        {
            // ピン番号がFirmataプロトコルの範囲外のとき
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        Write(0xF5, (byte)pin, (byte)state);
    }

    /// <summary>
    /// デジタルピンの状態を取得します。
    /// </summary>
    /// <param name="pin">状態を取得するピンの番号</param>
    /// <returns>デジタルピンの状態</returns>
    /// <exception cref="ArgumentOutOfRangeException">Firmata範囲(0~127) 外のピン番号を指定したとき</exception>
    /// <exception cref="InvalidOperationException">connect() を呼び出してArduinoに接続していないとき</exception>
    public PinState DigitalRead(int pin)
    {
        if (pin is not (>= 0 and < DIGITAL_PIN_COUNT))
        {
            // ピン番号がFirmataプロトコルの範囲外のとき
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        return _digitalStates[pin];
    }

    /// <summary>
    /// アナログピンの値を取得します。
    /// </summary>
    /// <param name="analogPin">状態を取得するアナログピンの番号</param>
    /// <returns>アナログピンの値</returns>
    /// <exception cref="ArgumentOutOfRangeException">Firmataプロトコルのアナログピンの範囲(0~15)を超えた範囲を指定した場合</exception>
    /// <exception cref="InvalidOperationException">Arduinoに接続していない状態でこの関数を呼び出したとき</exception>
    public int AnalogRead(int analogPin)
    {
        if (analogPin is not (>= 0 and < DIGITAL_PIN_COUNT))
        {
            // ピン番号がFirmataプロトコルの範囲外のとき
            throw new ArgumentOutOfRangeException(nameof(analogPin));
        }

        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        return _analogStates[analogPin];
    }

    public void AnalogWrite(int pin, int value)
    {
        if (pin is not (>= 0 and < DIGITAL_PIN_COUNT))
        {
            // ピン番号がFirmataプロトコルの範囲外のとき
            throw new ArgumentOutOfRangeException(nameof(pin));
        }

        if (value < 0) value = 0;

        if (_serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException("Not connected to Arduino, please call Connect() method to connect.");
        }

        var command = (byte)(ANALOG_MESSAGE + pin);
        Write([command, ..SplitBytes(value)]);
    }

    private void Write(params byte[] data)
    {
        DebugConsole.Debug($"Write data: {data.ToHexString()}");
        _serialPort.Write(data, 0, data.Length);
    }

    private void ReadDigitalState(int port)
    {
        var bytes = ReadCommand(2).ToArray();
        var pinStates = ConcatBytes(bytes[0], bytes[1]);

        for (var i = 0; i < 8; i++)
        {
            _digitalStates[port * 8 + i] = (PinState)((pinStates >> i) & 1);
        }
    }

    private void ReadAnalogValue(int pin)
    {
        var bytes = ReadCommand(2).ToArray();
        _analogStates[pin] = ConcatBytes(bytes[0], bytes[1]);
    }

    private IEnumerable<int> ReadSysexArgs()
    {
        while (true)
        {
            var data = _serialPort.ReadByte();

            if (data == SYSEX_END)
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

            DebugConsole.Debug($"コマンド受信: 0x{command:X2}");

            switch (command)
            {
                case SYSEX_BEGIN: // SysExコマンド
                {
                    var sysExCommand = _serialPort.ReadByte();
                    var sysExData = ReadSysexArgs().ToArray();

                    DebugConsole.Debug($"SysExコマンド受信: 0x{sysExCommand:X2}, データ: {sysExData.ToHexString()}");

                    continue;
                }
                case >= DIGITAL_MESSAGE and <= DIGITAL_MESSAGE + 0xF: // デジタルピン状態通知
                {
                    ReadDigitalState(command - 0x90);
                    break;
                }
                case >= ANALOG_MESSAGE and <= ANALOG_MESSAGE + 0xF: // アナログピン状態通知
                {
                    ReadAnalogValue(command - 0xA0);
                    break;
                }
            }
        }

        Console.WriteLine("Disconnected from Arduino.");
    }
}