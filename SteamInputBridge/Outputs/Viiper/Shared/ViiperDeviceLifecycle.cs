using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Outputs.Viiper.Shared;

internal static class ViiperDeviceLifecycle
{
    private const int CreateDeviceAttemptCount = 5;
    private static readonly TimeSpan CreateDeviceRetryDelay = TimeSpan.FromMilliseconds(25);

    // MARK: Publics
    // ========================================================================

    public static async Task<TOutput> ConnectAsync<TOutput>(
        ViiperOptions options,
        ViiperDeviceDefinition definition,
        Func<ViiperCreatedDevice, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createOutput);
        ViiperClient? client = new(options.Host, options.Port, options.Password);

        try
        {
            Device createdDevice = await CreateDeviceAsync(
                    client,
                    definition,
                    options.Logger,
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                TOutput output = await ConnectDeviceAsync(
                        client,
                        createdDevice,
                        definition,
                        options.Logger,
                        createOutput,
                        cancellationToken)
                    .ConfigureAwait(false);

                client = null;
                return output;
            }
            catch
            {
                if (client is not null)
                {
                    await RemoveCreatedDeviceAsync(
                            client,
                            createdDevice.BusID,
                            createdDevice.DevId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                throw;
            }
        }
        catch
        {
            client?.Dispose();
            throw;
        }
    }

    public static async Task ReclaimDevicesAsync(
        ViiperOptions options,
        ViiperDeviceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using ViiperClient client = new(options.Host, options.Port, options.Password);
        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);

        foreach (uint busId in buses.Buses)
        {
            await ReclaimOwnedDevicesAsync(
                    client,
                    definition,
                    options.Logger,
                    busId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // MARK: Privates
    // ========================================================================

    private static async Task<Device> CreateDeviceAsync(
        ViiperClient client,
        ViiperDeviceDefinition definition,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            uint busId = (await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false)).BusID;

            try
            {
                Device device = await AddDeviceAsync(
                        client,
                        busId,
                        definition,
                        cancellationToken)
                    .ConfigureAwait(false);

                ViiperOutputLog.CreatedDevice(logger, definition.DisplayName, device.BusID);
                return device;
            }
            catch (InvalidOperationException exception) when (
                attempt < CreateDeviceAttemptCount &&
                IsTransientCreateFailure(exception))
            {
                await RemoveBusIfPresentAsync(client, busId, cancellationToken).ConfigureAwait(false);
                await Task.Delay(CreateDeviceRetryDelay * attempt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await RemoveBusIfPresentAsync(client, busId, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static Task<Device> AddDeviceAsync(
        ViiperClient client,
        uint busId,
        ViiperDeviceDefinition definition,
        CancellationToken cancellationToken)
    {
        DeviceCreateRequest request = new()
        {
            Type = definition.DeviceType,
            IdVendor = definition.VendorId,
            IdProduct = definition.ProductId,
            DeviceSpecific = new Dictionary<string, object?>
            {
                ["name"] = definition.DisplayName,
            },
        };

        return client.BusDeviceAddAsync(busId, request, cancellationToken);
    }

    private static async Task RemoveBusIfPresentAsync(
        ViiperClient client,
        uint busId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.BusRemoveAsync(busId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private static bool IsTransientCreateFailure(InvalidOperationException exception)
    {
        return IsAutoAttachConflict(exception) || IsBusNotFound(exception);
    }

    private static bool IsAutoAttachConflict(InvalidOperationException exception)
    {
        return exception.Message.Contains("\"status\":409", StringComparison.Ordinal) &&
            exception.Message.Contains("Failed to auto-attach device", StringComparison.Ordinal);
    }

    private static bool IsBusNotFound(InvalidOperationException exception)
    {
        return exception.Message.Contains("\"status\":404", StringComparison.Ordinal) &&
            exception.Message.Contains("bus ", StringComparison.Ordinal) &&
            exception.Message.Contains(" not found", StringComparison.Ordinal);
    }

    private static async Task<TOutput> ConnectDeviceAsync<TOutput>(
        ViiperClient client,
        Device createdDevice,
        ViiperDeviceDefinition definition,
        ILogger? logger,
        Func<ViiperCreatedDevice, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ViiperDevice stream = await client.ConnectDeviceAsync(
                createdDevice.BusID,
                createdDevice.DevId,
                cancellationToken)
            .ConfigureAwait(false);

        ViiperOutputLog.ConnectedDevice(
            logger,
            definition.DisplayName,
            createdDevice.BusID,
            createdDevice.DevId);

#pragma warning disable CA2000 // Ownership transfers to the output wrapper.
        ViiperCreatedDevice outputDevice = new(
            client,
            stream,
            createdDevice.BusID,
            createdDevice.DevId,
            (busId, deviceId) => ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, busId, deviceId),
            (busId, deviceId) => ViiperOutputLog.DisconnectedDevice(logger, definition.DisplayName, busId, deviceId));
#pragma warning restore CA2000

        return createOutput(outputDevice);
    }

    private static async Task ReclaimOwnedDevicesAsync(
        ViiperClient client,
        ViiperDeviceDefinition definition,
        ILogger? logger,
        uint busId,
        CancellationToken cancellationToken)
    {
        DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken)
            .ConfigureAwait(false);
        int removedCount = 0;

        foreach (Device device in devices.Devices)
        {
            if (!definition.IsOwnedDevice(device))
            {
                continue;
            }

            _ = await client.BusDeviceRemoveAsync(device.BusID, device.DevId, cancellationToken)
                .ConfigureAwait(false);
            ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, device.BusID, device.DevId);
            removedCount++;
        }

        if (removedCount != 0 && removedCount == devices.Devices.Length)
        {
            _ = await client.BusRemoveAsync(busId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RemoveCreatedDeviceAsync(
        ViiperClient client,
        uint busId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.BusDeviceRemoveAsync(busId, deviceId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = await client.BusRemoveAsync(busId, cancellationToken).ConfigureAwait(false);
        }
    }
}
