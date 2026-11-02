using Insurance.Claims.Contracts;
using Insurance.Claims.Services;
using Xunit;

namespace Insurance.Claims.Tests;

public class ClaimStatusTransitionsTests
{
    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Paid)]
    public void The_workflow_moves_allowed(ClaimStatus from, ClaimStatus to)
    {
        Assert.True(ClaimStatusTransitions.CanMove(from, to));
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Approved)]      // can't skip review
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Paid)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Submitted)]     // can't "move" to the same status
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Paid)]        // must be approved first
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Submitted)]   // no going backwards
    [InlineData(ClaimStatus.Approved, ClaimStatus.Rejected)]       // approval can't be undone
    [InlineData(ClaimStatus.Approved, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Approved)]       // rejected is final
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Paid)]
    [InlineData(ClaimStatus.Paid, ClaimStatus.Submitted)]          // paid is final
    [InlineData(ClaimStatus.Paid, ClaimStatus.Approved)]
    public void Everything_else_is_blocked(ClaimStatus from, ClaimStatus to)
    {
        Assert.False(ClaimStatusTransitions.CanMove(from, to));
    }

    [Theory]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Paid)]
    public void Final_statuses_offer_no_next_step(ClaimStatus status)
    {
        Assert.Empty(ClaimStatusTransitions.NextFrom(status));
    }

    [Fact]
    public void Every_status_has_a_rule()
    {
        // Fails if someone adds a new ClaimStatus value 
        // but forgets to add it to the rule book.
        foreach (var status in Enum.GetValues<ClaimStatus>())
        {
            Assert.NotNull(ClaimStatusTransitions.NextFrom(status));
        }
    }
}