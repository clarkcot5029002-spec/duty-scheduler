namespace Duty.Core;

public static class PreferenceMarks
{
    public static byte[] CreateDefault()
    {
        var values = Enumerable.Repeat((byte)1, Week.SlotCount).ToArray();
        values[Week.SpecialSlot] = 0;
        return values;
    }

    public static byte Toggle(byte current, byte marker)
    {
        if (current > 2 || marker is not (0 or 2))
            throw new ArgumentException("只能设置优先安排或不可安排标记。");
        return current == marker ? (byte)1 : marker;
    }
}
