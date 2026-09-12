using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Refuses any request robots.txt doesn't allow for our product token, before it reaches the host. The
/// refusal is a <see cref="ConnectorException"/>, so it lands in the run results with its reason.
/// </summary>
internal sealed class RobotsHandler(RobotsTxtCache robots) : DelegatingHandler
{
    #region Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                  CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        request.Options.TryGetValue(RequestOptionKeys.Metrics, out var metrics);
        var policy = await robots.GetAsync(url, metrics, cancellationToken);
        if (policy.IsAllowed(url.PathAndQuery) is false)
        {
            throw new ConnectorException(policy.ExplainRefusal(url.PathAndQuery), url);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    #endregion Methods
}
