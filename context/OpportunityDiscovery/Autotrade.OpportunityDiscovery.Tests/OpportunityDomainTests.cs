using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityDomainTests
{
    [Fact]
    public void ResearchRun_RejectsInvalidRunningTransition()
    {
        var run = new ResearchRun("test", "[]", DateTimeOffset.UtcNow);
        run.MarkRunning(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => run.MarkRunning(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarketOpportunity_CanOnlyPublishApprovedUnexpiredOpportunity()
    {
        var opportunity = Opportunity(OpportunityStatus.Candidate, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => opportunity.Publish(DateTimeOffset.UtcNow));

        opportunity.Approve(DateTimeOffset.UtcNow);
        opportunity.Publish(DateTimeOffset.UtcNow);

        Assert.Equal(OpportunityStatus.Published, opportunity.Status);
    }

    [Fact]
    public void MarketOpportunity_CannotApproveExpiredOpportunity()
    {
        var opportunity = Opportunity(OpportunityStatus.Candidate, DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Throws<InvalidOperationException>(() => opportunity.Approve(DateTimeOffset.UtcNow));
    }

    private static MarketOpportunity Opportunity(
        OpportunityStatus status,
        DateTimeOffset validUntilUtc)
    {
        return new MarketOpportunity(
            Guid.NewGuid(),
            "market-1",
            OpportunityOutcomeSide.Yes,
            0.62m,
            0.7m,
            0.05m,
            validUntilUtc,
            "reason",
            "[]",
            "{}",
            "{}",
            "{}",
            status,
            DateTimeOffset.UtcNow);
    }
}
