using System.Collections.Concurrent;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// One request at a time per host, with at least the given interval between request starts. Shared by
/// every session in the run, so two schools on one server still reach it one request at a time.
/// </summary>
internal sealed class HostRateLimiter(TimeProvider timeProvider)
{
    #region State

    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    #endregion State

    #region Methods

    /// <summary>Waits for the host's turn. Every successful call must be paired with <see cref="Exit"/>.</summary>
    public async Task EnterAsync(Uri url, TimeSpan interval, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(url.Authority, _ => new HostGate());
        await gate.Turn.WaitAsync(cancellationToken);
        try
        {
            if (gate.LastStart is { } lastStart)
            {
                var wait = lastStart + interval - timeProvider.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, cancellationToken);
                }
            }

            gate.LastStart = timeProvider.GetUtcNow();
        }
        catch
        {
            gate.Turn.Release();
            throw;
        }
    }

    public void Exit(Uri url) => _gates[url.Authority].Turn.Release();

    #endregion Methods
}
