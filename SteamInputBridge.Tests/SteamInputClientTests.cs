using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamInputClientTests
{
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

    [TestMethod]
    public async Task OpenSteamConfigAsyncOpensControllerConfigUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.OpenSteamConfigAsync(456).ConfigureAwait(false);

        Assert.AreEqual("steam://controllerconfig/456", openedUrl?.AbsoluteUri);
    }

    [TestMethod]
    public async Task SteamUrlActionsValidateAppIdAndCancellation()
    {
        bool opened = false;
        SteamInputClient client = new((_, _) =>
        {
            opened = true;
            return ValueTask.CompletedTask;
        });

        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                async () => await client.ForceConfigAsync(0).ConfigureAwait(false))
            .ConfigureAwait(false);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await client.OpenSteamConfigAsync(1, cancellation.Token).ConfigureAwait(false))
            .ConfigureAwait(false);
        Assert.IsFalse(opened);
    }
}
