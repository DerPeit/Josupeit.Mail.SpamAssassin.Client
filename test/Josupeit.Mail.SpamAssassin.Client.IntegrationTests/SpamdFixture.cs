// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Net.Sockets;
using DotNet.Testcontainers.Configurations;

namespace Josupeit.Mail.SpamAssassin.Client.IntegrationTests;

// Builds an image with a real SpamAssassin in it and runs spamd for the length of the fixture. One container for the
// whole class, because starting spamd means compiling the stock rule set.
public abstract class SpamdFixture
{
    private const int SPAMD_PORT = 783;

    private readonly IFutureDockerImage _image = new ImageFromDockerfileBuilder().WithDockerfile("Dockerfile.spamd").Build();

    private IContainer _container = null!;

    [OneTimeSetUp]
    public async Task StartSpamd()
    {
        await _image.CreateAsync(TestContext.CurrentContext.CancellationToken);

        _container = new ContainerBuilder()
            .WithImage(_image)
            .WithPortBinding(SPAMD_PORT, assignRandomHostPort: true)

            // Readable by everyone, because spamd drops privileges to debian-spamd before it reads the site config.
            .WithResourceMapping("local.cf", "/etc/spamassassin/", UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)

            // spamd compiles the rule set before it listens, which is the slow part.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(SPAMD_PORT, o => o.WithTimeout(TimeSpan.FromMinutes(3))))
            .Build();

        await _container.StartAsync(TestContext.CurrentContext.CancellationToken);
    }

    [OneTimeTearDown]
    public async Task StopSpamd()
    {
        // The teardown runs even when the setup threw before the container existed, and a null here would replace the
        // Docker error that actually stopped the run with a NullReferenceException.
        if (_container is not null)
            await _container.DisposeAsync();

        await _image.DisposeAsync();
    }

    protected SpamAssassinClient CreateClient(SpamAssassinClientOptions? options = null) =>
        new(Connect, ConnectAsync, options);

    // The only place the daemon says why it refused something.
    protected async Task<string> ReadSpamdLog() =>
        (await _container.GetLogsAsync(ct: TestContext.CurrentContext.CancellationToken)).Stderr;

    private ISpamAssassinConnection Connect() =>
        new TcpSpamdConnection(new TcpClient(_container.Hostname, _container.GetMappedPublicPort(SPAMD_PORT)));

    private async Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = new TcpClient();

        try
        {
            await socket.ConnectAsync(_container.Hostname, _container.GetMappedPublicPort(SPAMD_PORT), cancellationToken);

            return new TcpSpamdConnection(socket);
        }
        catch
        {
            // A socket that failed to connect is still a socket, and nothing else is going to close it: the client
            // only ever disposes a connection it was handed.
            socket.Dispose();

            throw;
        }
    }
}
