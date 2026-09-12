namespace DueGooder.Infrastructure.Http;

/// <summary>How the dev response cache is used.</summary>
public enum ResponseCacheMode
{
    /// <summary>Every request goes to the network. Use this for measured runs.</summary>
    Off,

    /// <summary>Answer from the cache when possible; fetch and store on a miss.</summary>
    Use,

    /// <summary>Always fetch, and store every successful response, so a later run can replay it.</summary>
    Record,
}
