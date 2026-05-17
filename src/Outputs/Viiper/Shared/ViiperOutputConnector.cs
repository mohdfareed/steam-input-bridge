using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace Outputs.Viiper;

internal static class ViiperOutputConnector
{
    public static async Task<TOutput> ConnectAsync<TOutput>(
        ViiperOptions options,
        ViiperOutputDeviceDefinition definition,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        return await ConnectAsync(
                options,
                definition,
                ownership: null,
                createOutput,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<TOutput> ConnectExclusiveAsync<TOutput>(
        ViiperOptions options,
        ViiperOutputDeviceDefinition definition,
        string ownershipErrorMessage,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createOutput);

        ViiperOutputOwnership? ownership = ViiperOutputOwnership.AcquireOrThrow(
            definition.OwnershipName,
            ownershipErrorMessage);
        try
        {
            await ReclaimOwnedDevicesAsync(options, definition, cancellationToken).ConfigureAwait(false);
            return await ConnectAsync(options, definition, ownership, createOutput, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            ownership.Dispose();
            throw;
        }
    }

    public static async Task ReclaimOwnedDevicesAsync(
        ViiperOptions options,
        ViiperOutputDeviceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using ViiperClient client = new(options.Host, options.Port, options.Password);
        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);
        await ReclaimOwnedDevicesAsync(
                client,
                definition,
                options.Logger,
                buses.Buses,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<TOutput> ConnectAsync<TOutput>(
        ViiperOptions options,
        ViiperOutputDeviceDefinition definition,
        ViiperOutputOwnership? ownership,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ViiperClient? client = new(options.Host, options.Port, options.Password);

        try
        {
            Device createdDevice = await CreateDeviceAsync(
                client,
                definition,
                options.Logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                TOutput output = await ConnectDeviceAsync(
                    client,
                    createdDevice,
                    ownership,
                    definition,
                    options.Logger,
                    createOutput,
                    cancellationToken).ConfigureAwait(false);

                ownership = null;
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
            ownership?.Dispose();
            throw;
        }
    }

    private static async Task<Device> CreateDeviceAsync(
        ViiperClient client,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        uint busId = (await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false)).BusID;

        Device device;
        try
        {
            device = await client.BusDeviceAddAsync(
                busId,
                new DeviceCreateRequest
                {
                    Type = definition.DeviceType,
                    IdVendor = definition.VendorId,
                    IdProduct = definition.ProductId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = await client.BusRemoveAsync(busId, cancellationToken).ConfigureAwait(false);
            throw;
        }

        ViiperOutputLog.CreatedDevice(logger, definition.DisplayName, device.BusID);
        return device;
    }

    private static async Task ReclaimOwnedDevicesAsync(
        ViiperClient client,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        uint[] buses,
        CancellationToken cancellationToken)
    {
        foreach (uint busId in buses)
        {
            DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken).ConfigureAwait(false);
            int removedCount = 0;
            foreach (Device device in devices.Devices)
            {
                if (!definition.IsOwnedDevice(device))
                {
                    continue;
                }

                _ = await client.BusDeviceRemoveAsync(device.BusID, device.DevId, cancellationToken).ConfigureAwait(false);
                ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, device.BusID, device.DevId);
                removedCount++;
            }

            if (removedCount != 0 && removedCount == devices.Devices.Length)
            {
                _ = await client.BusRemoveAsync(busId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<TOutput> ConnectDeviceAsync<TOutput>(
        ViiperClient client,
        Device createdDevice,
        ViiperOutputOwnership? ownership,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ViiperDevice device = await client.ConnectDeviceAsync(
            createdDevice.BusID,
            createdDevice.DevId,
            cancellationToken).ConfigureAwait(false);

        ViiperOutputLog.ConnectedDevice(
            logger,
            definition.DisplayName,
            createdDevice.BusID,
            createdDevice.DevId);

#pragma warning disable CA2000 // Ownership transfers to the output instance created below.
        ViiperOutputSession session = new(
            client,
            device,
            createdDevice.BusID,
            createdDevice.DevId,
            ownership,
            (busId, deviceId) => ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, busId, deviceId),
            (busId, deviceId) => ViiperOutputLog.DisconnectedDevice(logger, definition.DisplayName, busId, deviceId));
#pragma warning restore CA2000

        return createOutput(session);
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
