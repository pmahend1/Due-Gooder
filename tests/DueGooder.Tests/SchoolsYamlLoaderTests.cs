using DueGooder.Infrastructure.Configuration;

namespace DueGooder.Tests;

public sealed class SchoolsYamlLoaderTests
{
    #region Methods

    [Fact]
    public void Loads_homepage_only_schools_for_discovery_and_configured_ones_with_their_options()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
                          """
                          schools:
                            - id: samford
                              name: Samford University
                              homepage: https://www.samford.edu/
                              timezone: America/Chicago
                            - id: okstate
                              name: Oklahoma State University
                              homepage: https://go.okstate.edu/
                              timezone: America/Chicago
                              platform: banner9
                              base_url: https://studentregistrationssb.okstate.edu/StudentRegistrationSsb/
                              mep_code: OSU
                          """);
        try
        {
            var schools = SchoolsYamlLoader.Load(path);

            Assert.Equal(2, schools.Count);
            Assert.False(schools[0].IsConfigured);
            Assert.Null(schools[0].BaseUrl);
            Assert.Equal(new Uri("https://www.samford.edu/"), schools[0].School.Homepage);
            Assert.True(schools[1].IsConfigured);
            Assert.Equal("OSU", schools[1].Options["mep_code"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion Methods
}
