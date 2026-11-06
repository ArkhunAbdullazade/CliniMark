using System.Net;
using System.Threading.RateLimiting;
using MedAnnotateApp.Presentation.Models;
using Microsoft.AspNetCore.Http;

namespace MedAnnotateApp.Tests;

public class RateLimitTests
{
    [Fact]
    public void LargeGroupOnOneNetworkCanJoinTogetherAndOtherNetworksStayIndependent()
    {
        using var limiter = Limiter(SignInRateLimit.DefaultPermitLimit);
        var hospital = Request("203.0.113.10");

        // A full group behind one address, each participant using every allowed attempt.
        Assert.Equal(78, SignInRateLimit.DefaultPermitLimit);
        for (var attempt = 0; attempt < SignInRateLimit.GroupSize * SignInRateLimit.AttemptsPerParticipant; attempt++)
        {
            using var lease = limiter.AttemptAcquire(hospital);
            Assert.True(lease.IsAcquired, $"Attempt {attempt + 1} was rejected.");
        }

        using (var rejected = limiter.AttemptAcquire(hospital)) Assert.False(rejected.IsAcquired);
        using var otherNetwork = limiter.AttemptAcquire(Request("198.51.100.7"));
        Assert.True(otherNetwork.IsAcquired);
    }

    [Fact]
    public void ConfiguredLimitIsApplied()
    {
        using var limiter = Limiter(2);
        var client = Request("203.0.113.20");

        using (var first = limiter.AttemptAcquire(client)) Assert.True(first.IsAcquired);
        using (var second = limiter.AttemptAcquire(client)) Assert.True(second.IsAcquired);
        using var third = limiter.AttemptAcquire(client);
        Assert.False(third.IsAcquired);
    }

    private static PartitionedRateLimiter<HttpContext> Limiter(int permitLimit) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => SignInRateLimit.Partition(context, permitLimit));

    private static DefaultHttpContext Request(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }
}
