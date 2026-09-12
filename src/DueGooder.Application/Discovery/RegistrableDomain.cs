namespace DueGooder.Application.Discovery;

/// <summary>
/// The part of a host name a school owns, e.g. <c>samford.edu</c> for <c>www.samford.edu</c>, so pages on
/// <c>registrar.samford.edu</c> count as the same site. Takes the last two labels, which fits .edu, .org and .ca schools.
/// </summary>
public static class RegistrableDomain
{
    #region Methods

    public static string Of(string host)
    {
        var labels = host.TrimEnd('.').ToLowerInvariant().Split('.');
        return labels.Length <= 2 || Uri.CheckHostName(host) is not UriHostNameType.Dns
            ? host.ToLowerInvariant()
            : $"{labels[^2]}.{labels[^1]}";
    }

    public static bool IsWithin(Uri url, string domain) =>
        url.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    #endregion Methods
}
