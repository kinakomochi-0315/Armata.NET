namespace Armata.NET;

public readonly record struct PinState
{
    public static readonly PinState High = new(true);
    public static readonly PinState Low = new(false);

    private readonly bool _value;

    private PinState(bool value)
    {
        _value = value;
    }

    public static implicit operator bool(PinState state)
    {
        return state._value;
    }

    public static implicit operator PinState(bool value)
    {
        return value ? High : Low;
    }

    public static explicit operator PinState(int value)
    {
        return value == 0 ? Low : High;
    }

    public static explicit operator int(PinState state)
    {
        return state._value ? 1 : 0;
    }
}