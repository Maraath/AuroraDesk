namespace AuroraDesk.Core.Native;

/// <summary>Estado de energia, para o modo economia na bateria.</summary>
public static class PowerStatus
{
    public static void Read(out bool hasBattery, out bool pluggedIn)
    {
        NativeMethods.GetSystemPowerStatus(out var s);
        hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        pluggedIn = s.ACLineStatus == 1;
    }
}
