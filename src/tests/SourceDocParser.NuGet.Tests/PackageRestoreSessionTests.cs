// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies that reference-pack screening needs metadata rather than package archives.</summary>
public sealed class PackageRestoreSessionTests
{
    /// <summary>An incompatible candidate is screened without downloading its archive.</summary>
    /// <param name="mapSource">Whether a competing source is excluded by package source mapping.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DependencyGroupsScreenIncompatibleCandidatesWithoutArchiveDownloads(bool mapSource)
    {
        await using var feed = new MetadataFeed(mapSource);
        using var session = new PackageRestoreSession(feed.Directory, NullLogger.Instance);

        var groups = await session.GetDependencyGroupsAsync(feed.PackageId, new(1, 0, 0), CancellationToken.None);

        await Assert.That(groups.Length).IsEqualTo(1);
        await Assert.That(groups[0].TargetFramework).IsEqualTo(NuGetFramework.ParseFolder("net9.0"));
        await Assert.That(DefaultCompatibilityProvider.Instance.IsCompatible(NuGetFramework.ParseFolder("net8.0"), groups[0].TargetFramework)).IsFalse();
        var dependency = await Assert.That(groups[0].Packages).HasSingleItem();
        await Assert.That(dependency.VersionRange).IsEqualTo(VersionRange.Parse("[3.0.0,4.0.0)"));
        await Assert.That(feed.Requests.Exists(static path => path.EndsWith(".nupkg", StringComparison.Ordinal))).IsFalse();
        await Assert.That(feed.Requests.Exists(static path => path.StartsWith("/excluded/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(feed.Requests.Exists(static path => path.StartsWith("/registration/", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>Serves NuGet registration metadata and refuses package archive requests.</summary>
    private sealed class MetadataFeed : IAsyncDisposable
    {
        /// <summary>Expected service and registration requests.</summary>
        private const int InitialRequestCapacity = 4;

        /// <summary>The temporary NuGet configuration.</summary>
        private readonly ScratchDirectory _directory = new("reference-metadata-feed");

        /// <summary>The loopback listener that owns its dynamically allocated port.</summary>
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        /// <summary>Stops the pending accept when the test completes.</summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>The request processing lifetime.</summary>
        private readonly Task _serving;

        /// <summary>The absolute base address used by NuGet service metadata.</summary>
        private readonly string _address;

        /// <summary>Initializes a new instance of the <see cref="MetadataFeed"/> class.</summary>
        /// <param name="mapSource">Whether to configure an excluded competing source.</param>
        public MetadataFeed(bool mapSource)
        {
            _listener.Start();
            _address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            var sources = new XElement("packageSources", new XElement("clear"));
            if (mapSource)
            {
                sources.Add(Source("excluded", $"{_address}/excluded/index.json"));
            }

            sources.Add(Source("fixture", $"{_address}/index.json"));
            var configuration = new XElement("configuration", sources);
            if (mapSource)
            {
                var pattern = new XElement("package", new XAttribute("pattern", PackageId));
                var mapping = new XElement("packageSource", new XAttribute("key", "fixture"), pattern);
                configuration.Add(new XElement("packageSourceMapping", new XElement("clear"), mapping));
            }

            new XDocument(configuration).Save(Path.Combine(_directory.Path, "NuGet.Config"));
            _serving = ServeAsync();
        }

        /// <summary>Gets the configuration directory.</summary>
        public string Directory => _directory.Path;

        /// <summary>Gets a package identity that cannot already exist in the global cache.</summary>
        public string PackageId { get; } = $"Reference.Metadata.{Guid.NewGuid():N}";

        /// <summary>Gets the URLs requested by NuGet.</summary>
        public List<string> Requests { get; } = [with(capacity: InitialRequestCapacity)];

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _serving;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                _listener.Dispose();
                _stop.Dispose();
                _directory.Dispose();
            }
        }

        /// <summary>Creates one loopback NuGet source declaration.</summary>
        /// <param name="name">Configured source name.</param>
        /// <param name="address">Service index URL.</param>
        /// <returns>The package source element.</returns>
        private static XElement Source(string name, string address) => new(
            "add",
            new XAttribute("key", name),
            new XAttribute("value", address),
            new XAttribute("allowInsecureConnections", true));

        /// <summary>Handles each request while the fixture is active.</summary>
        /// <returns>The server lifetime.</returns>
        private async Task ServeAsync()
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
                var request = await reader.ReadLineAsync(_stop.Token);
                if (request is null)
                {
                    continue;
                }

                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 })
                {
                    _stop.Token.ThrowIfCancellationRequested();
                }

                var path = request.Split(' ')[1];
                Requests.Add(path);
                var content = Response(path);
                var status = content is null ? "404 Not Found" : "200 OK";
                var body = Encoding.UTF8.GetBytes(content ?? string.Empty);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
        }

        /// <summary>Returns service or registration metadata without providing any archive.</summary>
        /// <param name="path">The requested URL path.</param>
        /// <returns>JSON metadata, or null when the resource is unavailable.</returns>
        private string? Response(string path) => path switch
        {
            "/index.json" => $$"""
                {"version":"3.0.0","resources":[
                  {"@id":"{{_address}}/registration/","@type":"RegistrationsBaseUrl/3.6.0"},
                  {"@id":"{{_address}}/flat/","@type":"PackageBaseAddress/3.0.0"}
                ]}
                """,
            _ when path.StartsWith("/registration/", StringComparison.Ordinal) => $$"""
                {"@id":"{{_address}}{{path}}","count":1,"items":[{
                  "@id":"{{_address}}{{path}}","count":1,"lower":"1.0.0","upper":"1.0.0","items":[{
                    "@id":"{{_address}}/registration/{{PackageId}}/1.0.0.json",
                    "packageContent":"{{_address}}/flat/{{PackageId}}/1.0.0/package.nupkg",
                    "catalogEntry":{"id":"{{PackageId}}","version":"1.0.0","listed":true,"authors":"Fixture",
                      "description":"Metadata only reference candidate","dependencyGroups":[{
                        "targetFramework":"net9.0","dependencies":[{"id":"Shared","range":"[3.0.0,4.0.0)"}]
                      }]}
                  }]
                }]}
                """,
            _ when path.StartsWith("/flat/", StringComparison.Ordinal) && path.EndsWith("/index.json", StringComparison.Ordinal) => "{\"versions\":[\"1.0.0\"]}",
            _ => null,
        };
    }
}
