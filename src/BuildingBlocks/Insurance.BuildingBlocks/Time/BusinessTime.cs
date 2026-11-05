using Microsoft.Extensions.Configuration;

namespace Insurance.BuildingBlocks.Time;

/// <summary>
/// The insurer's time zone: what "today" means for due dates, overdue premiums, coverage dates and claims, and the
/// zone times are written in on emails. Servers (and Azure) run on UTC, where "today" changes at 7 or 8 pm on the US
/// east coast, so a payment made in the evening would be dated tomorrow and a "due today" reminder would arrive the
/// evening before. Every service uses this instead of DateTime.UtcNow.Date.
///
/// Set with "BusinessTimeZone" (an IANA id; default America/New_York, which follows EST/EDT). The database side uses
/// the same zone through dbo.ufn_Today(), which DatabaseMigrator recreates from this setting at every start.
/// The browser still shows timestamps in the customer's own time zone.
/// </summary>
public static class BusinessTime
{
    public const string DefaultZoneId = "America/New_York";

    public static TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.FindSystemTimeZoneById(DefaultZoneId);

    /// <summary>Called by AddServiceDefaults (and the gateway); tests use the default.</summary>
    public static void Configure(IConfiguration config)
    {
        var id = config["BusinessTimeZone"];
        if (string.IsNullOrWhiteSpace(id)) return;
        Zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());   // throws on an unknown id: better than a silent UTC
    }

    /// <summary>Today's date in the business time zone.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>The current local time in the business time zone.</summary>
    public static DateTime Now => ToLocal(DateTime.UtcNow);

    public static DateOnly TodayAt(DateTimeOffset utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Zone).DateTime);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>"EST" or "EDT" for the eastern zone (DST-aware); the zone's own name for any other zone.</summary>
    public static string Abbreviation(DateTime local)
    {
        var name = Zone.IsDaylightSavingTime(local) ? Zone.DaylightName : Zone.StandardName;
        return name switch
        {
            "Eastern Standard Time" => "EST",
            "Eastern Daylight Time" => "EDT",
            "Central Standard Time" => "CST",
            "Central Daylight Time" => "CDT",
            "Mountain Standard Time" => "MST",
            "Mountain Daylight Time" => "MDT",
            "Pacific Standard Time" => "PST",
            "Pacific Daylight Time" => "PDT",
            _ => name
        };
    }

    /// <summary>The Windows name SQL Server's AT TIME ZONE expects, e.g. "Eastern Standard Time".</summary>
    public static string SqlServerZoneName =>
        Zone.HasIanaId && TimeZoneInfo.TryConvertIanaIdToWindowsId(Zone.Id, out var windowsId) ? windowsId : Zone.Id;
}
