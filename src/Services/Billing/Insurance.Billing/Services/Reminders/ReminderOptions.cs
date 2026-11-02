namespace Insurance.Billing.Services.Reminders;

/// <summary>"Reminders" section of the configuration.</summary>
public class ReminderOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often the job looks for reminders to send. Each reminder still goes out only once.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Wait after start-up before the first run, so the service (and the bus) are ready.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(30);
}
