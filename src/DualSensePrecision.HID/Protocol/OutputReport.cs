namespace DualSensePrecision.HID.Protocol;

/// <summary>An 11-byte adaptive trigger effect block (mode + 10 parameters).</summary>
public readonly struct TriggerEffect
{
    public const int Size = 11;
    private readonly byte[] _bytes;
    private TriggerEffect(byte[] bytes) => _bytes = bytes;
    public ReadOnlySpan<byte> Bytes => _bytes ?? Off().Bytes;

    public const byte ModeOff = 0x05;
    public const byte ModeFeedback = 0x21;
    public const byte ModeWeapon = 0x25;
    public const byte ModeVibration = 0x26;

    /// <summary>No resistance.</summary>
    public static TriggerEffect Off()
    {
        var b = new byte[Size];
        b[0] = ModeOff;
        return new TriggerEffect(b);
    }

    /// <summary>Constant resistance from <paramref name="position"/> (0–9) to the end, strength 1–8.</summary>
    public static TriggerEffect Feedback(int position, int strength)
    {
        position = Math.Clamp(position, 0, 9);
        strength = Math.Clamp(strength, 1, 8);
        return Zoned(ModeFeedback, position, strength, frequency: 0);
    }

    /// <summary>Resistance between start (2–7) and end (start+1–8) that "breaks" like a trigger pull. Strength 1–8.</summary>
    public static TriggerEffect Weapon(int start, int end, int strength)
    {
        start = Math.Clamp(start, 2, 7);
        end = Math.Clamp(end, start + 1, 8);
        strength = Math.Clamp(strength, 1, 8);
        var b = new byte[Size];
        int zones = (1 << start) | (1 << end);
        b[0] = ModeWeapon;
        b[1] = (byte)(zones & 0xFF);
        b[2] = (byte)((zones >> 8) & 0xFF);
        b[3] = (byte)(strength - 1);
        return new TriggerEffect(b);
    }

    /// <summary>Vibration from position (0–9) onward. Amplitude 1–8, frequency 1–255 Hz.</summary>
    public static TriggerEffect Vibration(int position, int amplitude, int frequency)
    {
        position = Math.Clamp(position, 0, 9);
        amplitude = Math.Clamp(amplitude, 1, 8);
        frequency = Math.Clamp(frequency, 1, 255);
        return Zoned(ModeVibration, position, amplitude, frequency);
    }

    /// <summary>
    /// Builds an effect from profile parameters. <paramref name="kind"/>: 0 Off, 1 Feedback, 2 Weapon, 3 Vibration
    /// (same order as the profile's AdaptiveEffectKind). Out-of-range values are clamped by each effect.
    /// </summary>
    public static TriggerEffect FromParameters(int kind, int start, int end, int strength, int frequency) => kind switch
    {
        1 => Feedback(start, strength),
        2 => Weapon(start, end, strength),
        3 => Vibration(start, strength, frequency),
        _ => Off(),
    };

    private static TriggerEffect Zoned(byte mode, int position, int strength, int frequency)
    {
        var b = new byte[Size];
        uint force = (uint)((strength - 1) & 0x07);
        uint active = 0, forceZones = 0;
        for (int i = position; i < 10; i++)
        {
            forceZones |= force << (3 * i);
            active |= 1u << i;
        }
        b[0] = mode;
        b[1] = (byte)(active & 0xFF);
        b[2] = (byte)((active >> 8) & 0xFF);
        b[3] = (byte)(forceZones & 0xFF);
        b[4] = (byte)((forceZones >> 8) & 0xFF);
        b[5] = (byte)((forceZones >> 16) & 0xFF);
        b[6] = (byte)((forceZones >> 24) & 0xFF);
        if (mode == ModeVibration) b[9] = (byte)frequency;
        return new TriggerEffect(b);
    }
}

/// <summary>
/// Builds a USB output report 0x02. Only fields whose "valid" flag is set are applied by the controller,
/// so callers set exactly what they want to change. Offsets include the report ID at [0].
/// </summary>
public sealed class OutputReportBuilder
{
    // Flag bytes
    private const int Flag0 = 1, Flag1 = 2, Flag2 = 39;
    // Fields
    private const int MotorRight = 3, MotorLeft = 4, MuteLed = 9;
    private const int RightTrigger = 11, LeftTrigger = 22;
    private const int PowerReduction = 37;
    private const int LightbarSetup = 42, LedBrightness = 43, PlayerLeds = 44, Red = 45, Green = 46, Blue = 47;

    // flag0
    private const byte CompatibleVibration = 0x01, HapticsSelect = 0x02, RightTriggerEnable = 0x04, LeftTriggerEnable = 0x08;
    // flag1
    private const byte MicMuteLedEnable = 0x01, LightbarEnable = 0x04, ReleaseLeds = 0x08, PlayerIndicatorEnable = 0x10, PowerReductionEnable = 0x02;
    // flag2
    private const byte LightbarSetupEnable = 0x02, CompatibleVibration2 = 0x04;

    private readonly byte[] _buf;

    public OutputReportBuilder(int reportLength = DualSenseProtocol.UsbOutputReportLength)
    {
        _buf = new byte[Math.Max(reportLength, DualSenseProtocol.UsbOutputReportLength)];
        _buf[0] = DualSenseProtocol.UsbOutputReportId;
    }

    public OutputReportBuilder Rumble(byte left, byte right, bool improvedEmulation)
    {
        _buf[Flag0] |= CompatibleVibration | HapticsSelect;
        if (improvedEmulation) _buf[Flag2] |= CompatibleVibration2;
        _buf[MotorLeft] = left;
        _buf[MotorRight] = right;
        return this;
    }

    /// <summary>Attenuation 0 (full) … 7 (weakest) for rumble and trigger motors.</summary>
    public OutputReportBuilder MotorPowerReduction(int rumble, int triggers)
    {
        _buf[Flag1] |= 0x40; // MOTOR_POWER_ENABLE
        _buf[PowerReduction] = (byte)(((Math.Clamp(rumble, 0, 7)) << 4) | Math.Clamp(triggers, 0, 7));
        return this;
    }

    public OutputReportBuilder Lightbar(byte r, byte g, byte b)
    {
        _buf[Flag1] |= LightbarEnable;
        _buf[Red] = r; _buf[Green] = g; _buf[Blue] = b;
        return this;
    }

    /// <summary>Fades out the firmware's default blue animation so a custom lightbar colour takes over.</summary>
    public OutputReportBuilder ReleaseDefaultLightbar()
    {
        _buf[Flag2] |= LightbarSetupEnable;
        _buf[LightbarSetup] = 0x02; // LIGHT_OUT
        return this;
    }

    /// <summary>Player indicator: 5-bit mask (bit0 = leftmost LED). Brightness 0 high, 1 mid, 2 low.</summary>
    public OutputReportBuilder PlayerIndicators(byte mask, int brightness = 0, bool instant = true)
    {
        _buf[Flag1] |= PlayerIndicatorEnable;
        _buf[PlayerLeds] = (byte)((mask & 0x1F) | (instant ? 0x20 : 0));
        _buf[LedBrightness] = (byte)Math.Clamp(brightness, 0, 2);
        return this;
    }

    /// <summary>Mute LED: 0 off, 1 on, 2 pulsing.</summary>
    public OutputReportBuilder MuteLight(int mode)
    {
        _buf[Flag1] |= MicMuteLedEnable;
        _buf[MuteLed] = (byte)Math.Clamp(mode, 0, 2);
        return this;
    }

    public OutputReportBuilder LeftTriggerEffect(TriggerEffect e)
    {
        _buf[Flag0] |= LeftTriggerEnable;
        e.Bytes.CopyTo(_buf.AsSpan(LeftTrigger, TriggerEffect.Size));
        return this;
    }

    public OutputReportBuilder RightTriggerEffect(TriggerEffect e)
    {
        _buf[Flag0] |= RightTriggerEnable;
        e.Bytes.CopyTo(_buf.AsSpan(RightTrigger, TriggerEffect.Size));
        return this;
    }

    public byte[] Build() => (byte[])_buf.Clone();
}
