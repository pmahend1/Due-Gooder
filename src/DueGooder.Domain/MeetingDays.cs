namespace DueGooder.Domain;

/// <summary>
/// Days a meeting occurs. <see cref="None"/> means the source says there are no set days
/// (e.g. asynchronous online), which is different from days that are missing.
/// </summary>
[Flags]
public enum MeetingDays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
}
