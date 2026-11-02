using Microsoft.Extensions.Options;

namespace Insurance.Billing.Services.Reminders;

/// <summary>
/// Runs <see cref="IPaymentReminderService"/> on a timer. Safe with several Billing instances: each reminder is
/// recorded under a primary key (installment, stage) before its event is published, so only one instance sends it.
/// </summary>
public class PaymentReminderJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ReminderOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentReminderJob> _logger;

    public PaymentReminderJob(IServiceScopeFactory scopes, IOptions<ReminderOptions> options, TimeProvider clock,
        ILogger<PaymentReminderJob> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment reminders are disabled (Reminders:Enabled = false).");
            return;
        }

        try { await Task.Delay(_options.StartupDelay, _clock, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_options.Interval, _clock);
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IPaymentReminderService>();
                await reminders.SendDueRemindersAsync(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // E.g. Enrollments or the database is down. Nothing was recorded, so the next run tries again.
                _logger.LogWarning(ex, "Sending payment reminders failed; will retry on the next run.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
