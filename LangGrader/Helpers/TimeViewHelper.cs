namespace LangGrader.Helpers;

public static class TimeViewHelper
{
    private static readonly TimeZoneInfo KoreaTimeZone = GetKoreaTimeZone();

    public static DateTime ToKst(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind switch
        {
            DateTimeKind.Utc => utcDateTime,
            DateTimeKind.Local => utcDateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, KoreaTimeZone);
    }

    public static string FormatKst(DateTime utcDateTime)
    {
        return ToKst(utcDateTime).ToString("yyyy-MM-dd HH:mm:ss 'KST'");
    }

    public static string FormatKstMinute(DateTime utcDateTime)
    {
        return ToKst(utcDateTime).ToString("yyyy-MM-dd HH:mm 'KST'");
    }

    private static TimeZoneInfo GetKoreaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        }
    }
}