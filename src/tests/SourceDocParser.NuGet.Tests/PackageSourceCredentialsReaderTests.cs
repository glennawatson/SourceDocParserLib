// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="Readers.PackageSourceCredentialsReader"/> on the
/// with-creds fixture (env-var expansion of GitHub PAT) plus
/// inline XML for the spaces-in-source-name case.
/// </summary>
public class PackageSourceCredentialsReaderTests
{
    /// <summary>
    /// The fixture's <c>github</c> source carries
    /// <c>%GITHUB_TOKEN%</c>; with that env var set the reader
    /// returns the expanded value.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExpandsEnvVarInClearTextPassword()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-creds", "nuget.config");
        var original = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "ghp_secret_value_xyz");

            var creds = await Readers.PackageSourceCredentialsReader.ReadAsync(path).ConfigureAwait(false);

            await Assert.That(creds.ContainsKey("github")).IsTrue();
            var github = creds["github"];
            await Assert.That(github.Username).IsEqualTo("ci-bot");
            await Assert.That(github.ClearTextPassword).IsEqualTo("ghp_secret_value_xyz");
            await Assert.That(github.ValidAuthenticationTypes).IsEqualTo("basic");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", original);
        }
    }

    /// <summary>
    /// Source names with spaces use NuGet's <c>_x0020_</c> XML
    /// escape — the reader unescapes back to the friendly name.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnescapesSpacesInSourceName()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSourceCredentials>
                <My_x0020_Feed>
                  <add key="Username" value="user" />
                  <add key="ClearTextPassword" value="secret" />
                </My_x0020_Feed>
              </packageSourceCredentials>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var creds = await Readers.PackageSourceCredentialsReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(creds.ContainsKey("My Feed")).IsTrue();
    }

    /// <summary>
    /// An unresolved <c>%VAR%</c> stays literal so the caller
    /// can detect missing config rather than silently sending
    /// an empty password.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnresolvedEnvVarStaysLiteral()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSourceCredentials>
                <my>
                  <add key="Username" value="u" />
                  <add key="ClearTextPassword" value="%THIS_VAR_DOES_NOT_EXIST_xyz%" />
                </my>
              </packageSourceCredentials>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var creds = await Readers.PackageSourceCredentialsReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(creds["my"].ClearTextPassword).IsEqualTo("%THIS_VAR_DOES_NOT_EXIST_xyz%");
    }

    /// <summary>
    /// A source block missing the password is skipped — partial
    /// credentials are not surfaced.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipsSourceMissingPassword()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSourceCredentials>
                <halfbaked>
                  <add key="Username" value="u" />
                </halfbaked>
              </packageSourceCredentials>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var creds = await Readers.PackageSourceCredentialsReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(creds.Count).IsEqualTo(0);
    }
}
