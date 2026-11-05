using Insurance.BuildingBlocks.Time;
using System.Text.Json;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Catalog.Contracts;
using Insurance.Quotes.DTOs;
using Insurance.Quotes.Models;
using Insurance.Quotes.Rating;
using Insurance.Quotes.Repositories;

namespace Insurance.Quotes.Services;

public class QuoteService : IQuoteService
{
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromDays(30);

    private readonly IPlanCatalog _plans;
    private readonly IQuoteRepository _quotes;
    private readonly IReadOnlyDictionary<PolicyType, IRatingStrategy> _strategies;

    // DI hands us EVERY registered IRatingStrategy; we index them by the policy type they handle.
    public QuoteService(IPlanCatalog plans, IQuoteRepository quotes, IEnumerable<IRatingStrategy> strategies)
    {
        _plans = plans;
        _quotes = quotes;
        _strategies = strategies.ToDictionary(s => s.PolicyType);
    }

    public async Task<IReadOnlyList<QuestionDefinition>> GetQuestionsAsync(int policyId)
    {
        var policy = await GetActivePolicyAsync(policyId);
        return StrategyFor(policy).Questions;
    }

    public async Task<QuoteDto> CreateAsync(int userId, QuoteRequest request)
    {
        var policy = await GetActivePolicyAsync(request.PolicyId);
        var strategy = StrategyFor(policy);

        var answers = AnswerSet.From(strategy.Questions, request.Answers);
        var today = BusinessTime.Today;

        // The plan's MonthlyPremium is the BASE rate. A BusinessRuleException here means "declined".
        var result = strategy.Rate(policy.MonthlyPremium, answers, today);

        var id = await _quotes.CreateAsync(
            userId, policy.PolicyId,
            JsonSerializer.Serialize(answers.Values),
            JsonSerializer.Serialize(result.Factors),
            result.BasePremium, result.MonthlyPremium,
            DateTime.UtcNow.Add(QuoteLifetime));

        var saved = await _quotes.GetByIdAsync(id);
        return MapToDto(saved!, policy);
    }

    public async Task<QuoteDto?> GetByIdAsync(int quoteId, int userId)
    {
        var quote = await _quotes.GetByIdAsync(quoteId);
        if (quote is null || quote.UserId != userId) return null;   // same null for "missing" and "not yours"

        // Plans are only ever deactivated, never deleted, so the quote's plan is always there.
        var policy = await _plans.GetAsync(quote.PolicyId)
            ?? throw new InvalidOperationException($"Quote {quoteId} refers to missing plan {quote.PolicyId}.");
        return MapToDto(quote, policy);
    }

    private async Task<PlanInfo> GetActivePolicyAsync(int policyId)
    {
        var policy = await _plans.GetAsync(policyId);
        if (policy is null || !policy.IsActive)
            throw new NotFoundException("That policy is not available.");
        return policy;
    }

    private IRatingStrategy StrategyFor(PlanInfo policy) =>
        _strategies.TryGetValue(policy.PolicyType, out var strategy)
            ? strategy
            : throw new BusinessRuleException($"Online quotes aren't available for {policy.PolicyType} policies yet.");

    private static QuoteDto MapToDto(Quote q, PlanInfo plan) => new()
    {
        QuoteId = q.QuoteId,
        PolicyId = q.PolicyId,
        PolicyName = plan.PolicyName,
        PolicyType = plan.PolicyType,
        CoverageAmount = plan.CoverageAmount,
        TermMonths = plan.TermMonths,
        BasePremium = q.BasePremium,
        MonthlyPremium = q.MonthlyPremium,
        TotalOverTerm = q.MonthlyPremium * plan.TermMonths,
        Factors = JsonSerializer.Deserialize<List<RatingFactor>>(q.Breakdown) ?? new List<RatingFactor>(),
        ExpiresAt = q.ExpiresAt,
        IsExpired = q.ExpiresAt < DateTime.UtcNow,
        IsUsed = q.EnrollmentId is not null || q.ReservedUntil > DateTime.UtcNow
    };
}