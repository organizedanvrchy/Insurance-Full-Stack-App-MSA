using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Enrollments.Contracts;
using Insurance.Enrollments.DTOs;
using Insurance.Enrollments.Enums;
using Insurance.Enrollments.Exceptions;
using Insurance.Enrollments.Models;
using Insurance.Enrollments.Services;
using Insurance.Identity.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Enrollments.Tests;

public class EnrollmentServiceTests
{
    private static DateOnly Today => Insurance.BuildingBlocks.Time.BusinessTime.Today;

    private readonly FakeEnrollmentRepository _repo = new();
    private readonly FakeQuoteReservations _quotes = new();
    private readonly FakePlanCatalog _plans = new();
    private readonly FakePremiumSummaries _premiums = new();
    private readonly FakeUserDirectory _users = new();
    private readonly EnrollmentService _sut;

    public EnrollmentServiceTests()
    {
        var options = Options.Create(new EnrollmentOptions
        {
            SagaTimeout = TimeSpan.FromMilliseconds(200),
            SagaPollInterval = TimeSpan.FromMilliseconds(10)
        });
        _sut = new EnrollmentService(_repo, _quotes, _plans, _premiums, _users, options, NullLogger<EnrollmentService>.Instance);
    }

    // Start date window

    [Theory]
    [InlineData(-1)]    // yesterday is allowed: one day of slack for time zones
    [InlineData(0)]
    [InlineData(90)]    // the furthest allowed
    public async Task Start_dates_inside_the_window_are_accepted(int daysFromToday)
    {
        var start = Today.AddDays(daysFromToday);

        var dto = await _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7, StartDate = start });

        Assert.Equal(start, dto.StartDate);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(91)]
    public async Task Start_dates_outside_the_window_are_rejected(int daysFromToday)
    {
        var request = new EnrollmentRequest { QuoteId = 7, StartDate = Today.AddDays(daysFromToday) };

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.EnrollAsync(1, request));
        Assert.Null(_repo.LastCreate);    // and nothing was sent to the database
    }

    [Fact]
    public async Task Enrolling_reserves_the_quote_for_the_caller_and_takes_the_plan_and_premium_from_it()
    {
        await _sut.EnrollAsync(5, new EnrollmentRequest { QuoteId = 42, StartDate = Today });

        Assert.Equal((42, 5), _quotes.LastReserve);
        var created = _repo.LastCreate!.Value;
        Assert.Equal(5, created.UserId);
        Assert.Equal(1, created.PolicyId);          // from the quote
        Assert.Equal(42, created.QuoteId);          // the saga needs it to confirm or release the quote
        Assert.Equal(100m, created.MonthlyPremium); // the quoted price, not the plan's base rate
        Assert.Equal(12, created.TermMonths);       // from the plan
    }

    [Fact]
    public async Task A_quote_that_cannot_be_used_stops_before_anything_is_created()
    {
        _quotes.RejectWith = new BusinessRuleException("This quote has expired. Please request a new quote.");

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 }));
        Assert.Null(_repo.LastCreate);
    }

    // The enrollment saga

    [Fact]
    public async Task When_the_saga_completes_the_customer_gets_the_active_policy()
    {
        _premiums.Summaries[100] = new PremiumSummary(0, 12, 0, Today, 100m);

        var dto = await _sut.EnrollAsync(5, new EnrollmentRequest { QuoteId = 42, StartDate = Today });

        Assert.Equal(EnrollmentStatus.Active, dto.Status);
        Assert.Equal("Test Plan", dto.PolicyName);   // from the Catalog service
        Assert.Equal(12, dto.PaymentsTotal);         // from the Billing service
    }

    [Fact]
    public async Task If_billing_is_slow_the_customer_gets_the_pending_enrollment_instead_of_an_error()
    {
        _repo.SagaOutcome = null;   // stays Pending

        var dto = await _sut.EnrollAsync(5, new EnrollmentRequest { QuoteId = 42, StartDate = Today });

        Assert.Equal(EnrollmentStatus.Pending, dto.Status);   // the controller answers 202 Accepted
    }

    [Fact]
    public async Task If_billing_is_down_policies_still_show_without_payment_progress()
    {
        _repo.Enrollments.Add(new Enrollment { EnrollmentId = 1, UserId = 1, PolicyId = 1, Status = EnrollmentStatus.Active });
        _premiums.Unavailable = true;

        var dto = (await _sut.GetMineAsync(1)).Single();

        Assert.Equal("Test Plan", dto.PolicyName);
        Assert.Equal(0, dto.PaymentsTotal);
        Assert.Equal(PaymentAlert.None, dto.PaymentAlert);
    }

    [Fact]
    public async Task If_the_saga_failed_enrolling_fails()
    {
        _repo.SagaOutcome = EnrollmentStatus.Failed;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.EnrollAsync(5, new EnrollmentRequest { QuoteId = 42 }));
    }

    [Fact]
    public async Task A_plan_retired_since_the_quote_releases_the_quote()
    {
        _plans.Plans[1] = _plans.Plans[1] with { IsActive = false };

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 }));
        Assert.Null(_repo.LastCreate);
        Assert.Equal(new[] { 7 }, _quotes.Released);
    }

    [Fact]
    public async Task A_duplicate_enrollment_releases_the_quote()
    {
        await _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 });

        await Assert.ThrowsAsync<DuplicateEnrollmentException>(() => _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 8 }));
        Assert.Equal(new[] { 8 }, _quotes.Released);
    }

    [Fact]
    public async Task A_pending_enrollment_already_blocks_a_second_one_for_the_same_plan()
    {
        _repo.SagaOutcome = null;
        await _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 });

        await Assert.ThrowsAsync<DuplicateEnrollmentException>(() => _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 8 }));
    }

    // Payment alerts

    // Enrollment 1 on plan 1, with its payment progress as Billing would report it.
    private Enrollment Active(int overdue = 0, int? nextDueInDays = null)
    {
        _premiums.Summaries[1] = new PremiumSummary(
            PaymentsPaid: 0, PaymentsTotal: 12, OverdueCount: overdue,
            NextDueDate: nextDueInDays is { } days ? Today.AddDays(days) : null, NextDueAmount: null);

        return new Enrollment { EnrollmentId = 1, UserId = 1, PolicyId = 1, Status = EnrollmentStatus.Active };
    }

    private async Task<PaymentAlert> AlertFor(Enrollment enrollment)
    {
        _repo.Enrollments.Add(enrollment);
        var list = await _sut.GetMineAsync(1);
        return list.Single().PaymentAlert;
    }

    [Fact]
    public async Task Overdue_installments_raise_an_overdue_alert()
    {
        Assert.Equal(PaymentAlert.Overdue, await AlertFor(Active(overdue: 2, nextDueInDays: 30)));
    }

    [Theory]
    [InlineData(0, PaymentAlert.DueSoon)]     // due today
    [InlineData(3, PaymentAlert.DueSoon)]
    [InlineData(7, PaymentAlert.DueSoon)]     // last day of the window
    [InlineData(8, PaymentAlert.None)]        // just outside it
    [InlineData(30, PaymentAlert.None)]
    public async Task The_due_soon_window_is_seven_days(int nextDueInDays, PaymentAlert expected)
    {
        Assert.Equal(expected, await AlertFor(Active(nextDueInDays: nextDueInDays)));
    }

    [Fact]
    public async Task A_fully_paid_policy_has_no_alert()
    {
        Assert.Equal(PaymentAlert.None, await AlertFor(Active()));     // no next due date at all
    }

    [Fact]
    public async Task A_cancelled_policy_has_no_alert_even_with_overdue_history()
    {
        var cancelled = Active(overdue: 2, nextDueInDays: 1);
        cancelled.Status = EnrollmentStatus.Cancelled;

        Assert.Equal(PaymentAlert.None, await AlertFor(cancelled));
    }

    // Who can see an enrollment

    [Fact]
    public async Task The_owner_can_see_their_enrollment()
    {
        _repo.Enrollments.Add(Active());

        Assert.NotNull(await _sut.GetByIdAsync(1, userId: 1, isAdmin: false));
    }

    [Fact]
    public async Task Another_customer_gets_nothing_so_ids_cannot_be_probed()
    {
        _repo.Enrollments.Add(Active());

        Assert.Null(await _sut.GetByIdAsync(1, userId: 2, isAdmin: false));
    }

    [Fact]
    public async Task An_admin_can_see_any_enrollment()
    {
        _repo.Enrollments.Add(Active());

        Assert.NotNull(await _sut.GetByIdAsync(1, userId: 99, isAdmin: true));
    }

    // Cancel

    [Fact]
    public async Task The_owner_can_cancel_an_active_enrollment()
    {
        _repo.Enrollments.Add(Active());

        Assert.True(await _sut.CancelAsync(1, userId: 1));
        Assert.Equal(EnrollmentStatus.Cancelled, _repo.Enrollments.Single().Status);
        Assert.Equal(new[] { 1 }, _repo.CancelledEvents);   // Billing hears about it and drops the unpaid installments
    }

    [Fact]
    public async Task Someone_else_cannot_cancel_it()
    {
        _repo.Enrollments.Add(Active());

        Assert.False(await _sut.CancelAsync(1, userId: 2));
        Assert.Equal(EnrollmentStatus.Active, _repo.Enrollments.Single().Status);
        Assert.Empty(_repo.CancelledEvents);
    }

    // ---------------- Email verification ----------------

    [Fact]
    public async Task A_customer_must_verify_their_email_before_enrolling()
    {
        _users.Unverified.Add(1);

        var ex = await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 }));

        Assert.IsAssignableFrom<ForbiddenException>(ex);          // a 403
        Assert.Contains("enroll in a policy", ex.Message);
        Assert.Null(_quotes.LastReserve);                          // the quote is never touched
        Assert.Null(_repo.LastCreate);
    }

    [Fact]
    public async Task Enrolling_fails_cleanly_when_the_identity_service_is_down()
    {
        _users.Unavailable = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.EnrollAsync(1, new EnrollmentRequest { QuoteId = 7 }));
        Assert.Null(_quotes.LastReserve);
    }
}
