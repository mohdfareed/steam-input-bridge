using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

/// <summary>Tests Steam Input control.</summary>
[TestClass]
public sealed class SteamInputClientTests
{
    /// <summary>Checks force and clear URL shapes.</summary>
    [TestMethod]
    public async Task ForceConfigAsyncOpensExpectedUrls()
    {
        List<Uri> openedUrls = [];
        SteamInputClient client = new((url, _) =>
        {
            openedUrls.Add(url);
            return ValueTask.CompletedTask;
        });

        await client.ForceConfigAsync(123).ConfigureAwait(false);
        await client.ForceConfigAsync(null).ConfigureAwait(false);

        Assert.HasCount(2, openedUrls);
        Assert.AreEqual("steam://forceinputappid/123", openedUrls[0].AbsoluteUri);
        Assert.AreEqual("steam://forceinputappid/0", openedUrls[1].AbsoluteUri);
    }

    /// <summary>Checks force input validation.</summary>
    [TestMethod]
    public async Task ForceConfigAsyncRejectsZeroAppId()
    {
        SteamInputClient client = new();

        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                async () => await client.ForceConfigAsync(0).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    /// <summary>Checks controller configurator app id dispatch.</summary>
    [TestMethod]
    public async Task OpenControllerConfigAsyncOpensControllerConfigAppId()
    {
        uint? openedAppId = null;
        SteamInputClient client = new(openControllerConfig: (appId, _) =>
        {
            openedAppId = appId;
            return ValueTask.CompletedTask;
        });

        await client.OpenControllerConfigAsync(123).ConfigureAwait(false);

        Assert.AreEqual<uint?>(123, openedAppId);
    }

    /// <summary>Checks the desktop action uses Steam's desktop config app id.</summary>
    [TestMethod]
    public async Task OpenSteamControllerDesktopConfigAsyncOpensDesktopConfigAppId()
    {
        uint? openedAppId = null;
        SteamInputClient client = new(openControllerConfig: (appId, _) =>
        {
            openedAppId = appId;
            return ValueTask.CompletedTask;
        });

        await client.OpenSteamControllerDesktopConfigAsync().ConfigureAwait(false);

        Assert.AreEqual<uint?>(SteamInputClient.DesktopConfigAppId, openedAppId);
    }

    /// <summary>Checks Steam app id environment resolution.</summary>
    [TestMethod]
    public void ResolveAppIdFromEnvironmentReadsSteamAppId()
    {
        string? previousSteamAppId = Environment.GetEnvironmentVariable("SteamAppId");
        string? previousSteamGameId = Environment.GetEnvironmentVariable("SteamGameId");
        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", "456");
            Environment.SetEnvironmentVariable("SteamGameId", "789");

            Assert.AreEqual<uint?>(456, SteamInputClient.ResolveAppId());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SteamAppId", previousSteamAppId);
            Environment.SetEnvironmentVariable("SteamGameId", previousSteamGameId);
        }
    }

    /// <summary>Checks cancellation before opening a URL.</summary>
    [TestMethod]
    public async Task ForceConfigAsyncHonorsCancellation()
    {
        bool opened = false;
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(false);
        SteamInputClient client = new((_, _) =>
        {
            opened = true;
            return ValueTask.CompletedTask;
        });

        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await client.ForceConfigAsync(123, cancellation.Token).ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.IsFalse(opened);
    }
}
