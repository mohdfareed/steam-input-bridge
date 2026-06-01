using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class TeensyMouseOutputTests
{
    [TestMethod]
    public void WriteMouseReportEncodesFrameAndClampsDeltas()
    {
        byte[] frame = new byte[TeensyProtocol.FrameSize];
        MouseReport report = new(
            MouseButtons.Left | MouseButtons.Back,
            DeltaX: int.MaxValue,
            DeltaY: int.MinValue,
            WheelDelta: 120);

        int bytes = TeensyProtocol.WriteMouseReport(frame, sequence: 7, report);

        Assert.AreEqual(TeensyProtocol.FrameSize, bytes);
        CollectionAssert.AreEqual(new byte[] { (byte)'S', (byte)'I', (byte)'B', 1, 1, 7, 8 }, frame[..7]);
        Assert.AreEqual((ushort)(MouseButtons.Left | MouseButtons.Back), BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)));
        Assert.AreEqual(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(9)));
        Assert.AreEqual(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(11)));
        Assert.AreEqual((short)120, BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(13)));
        Assert.AreEqual(
            TeensyProtocol.ComputeCrc16(frame.AsSpan(0, TeensyProtocol.HeaderSize + TeensyProtocol.MousePayloadSize)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(15)));
    }

    [TestMethod]
    public void HandshakeProbeEncodesFrameAndValidatesResponse()
    {
        byte[] probe = new byte[TeensyProtocol.HandshakeProbeFrameSize];

        int bytes = TeensyProtocol.WriteHandshakeProbe(probe, sequence: 4);

        Assert.AreEqual(TeensyProtocol.HandshakeProbeFrameSize, bytes);
        CollectionAssert.AreEqual(new byte[] { (byte)'S', (byte)'I', (byte)'B', 1, 0, 4, 0 }, probe[..7]);
        Assert.AreEqual(
            TeensyProtocol.ComputeCrc16(probe.AsSpan(0, TeensyProtocol.HeaderSize)),
            BinaryPrimitives.ReadUInt16LittleEndian(probe.AsSpan(7)));

        byte[] response =
        [
            (byte)'S',
            (byte)'I',
            (byte)'B',
            1,
            0x80,
            4,
            4,
            (byte)'T',
            (byte)'N',
            (byte)'S',
            (byte)'Y',
            0,
            0,
        ];
        ushort checksum = TeensyProtocol.ComputeCrc16(
            response.AsSpan(0, TeensyProtocol.HeaderSize + TeensyProtocol.HandshakeResponsePayloadSize));
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(11), checksum);

        Assert.IsTrue(TeensyProtocol.IsHandshakeResponse(response, sequence: 4));
        Assert.IsFalse(TeensyProtocol.IsHandshakeResponse(response, sequence: 5));
    }

    [TestMethod]
    public void OrderCandidatePortsPrioritizesLikelyTeensyPorts()
    {
        IReadOnlyList<string> ports = TeensyPortDiscovery.OrderCandidatePorts(
            ["COM3", "COM7", "COM9"],
            ["COM7", "COM4"],
            configuredPort: null);

        CollectionAssert.AreEqual(new[] { "COM7", "COM4", "COM3", "COM9" }, new List<string>(ports));
    }

    [TestMethod]
    public void OrderCandidatePortsUsesConfiguredPortOnly()
    {
        IReadOnlyList<string> ports = TeensyPortDiscovery.OrderCandidatePorts(
            ["COM3", "COM7"],
            ["COM7"],
            configuredPort: " COM9 ");

        CollectionAssert.AreEqual(new[] { "COM9" }, new List<string>(ports));
    }

    [TestMethod]
    public async Task ServiceSearchesConnectsAndReturnsToConnectingAfterDisconnect()
    {
        using TestContext context = CreateService(port: null);
        context.Discovery.Ports.Add("COM7");
        context.Connection.ConnectAllowed = true;

        await context.Service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => context.Service.Status.State == TeensyConnectionState.Connected).ConfigureAwait(false);

        context.Discovery.Ports.Clear();
        await WaitUntilAsync(() => context.Service.Status.State == TeensyConnectionState.Connecting).ConfigureAwait(false);

        await context.Service.StopAsync(default).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ServiceReconnectsWhenConfiguredPortChanges()
    {
        using TestContext context = CreateService(port: "COM3");
        context.Discovery.Ports.Add("COM3");
        context.Connection.ConnectAllowed = true;

        await context.Service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => context.Service.Status.ConnectedPort == "COM3").ConfigureAwait(false);

        SteamInputBridgeSettings changed = new();
        changed.Teensy.Port = "COM9";
        changed.Games["game"] = new GameProfile { Executable = @"C:\Game\game.exe" };
        context.Discovery.Ports.Clear();
        context.Discovery.Ports.Add("COM9");
        context.Monitor.Set(changed);

        await WaitUntilAsync(() => context.Service.Status.ConnectedPort == "COM9").ConfigureAwait(false);

        await context.Service.StopAsync(default).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MouseOutputClearSendsZeroReport()
    {
        using TestContext context = CreateService(port: null);
        context.Discovery.Ports.Add("COM7");
        context.Connection.ConnectAllowed = true;
        await context.Service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => context.Service.IsConnected).ConfigureAwait(false);

        await using IMouseOutput output = context.Service.CreateOutput();
        await output.ClearAsync().ConfigureAwait(false);

        await WaitUntilAsync(() => context.Connection.LastWrite.Length == TeensyProtocol.FrameSize).ConfigureAwait(false);

        Assert.AreEqual(TeensyProtocol.FrameSize, context.Connection.LastWrite.Length);
        Assert.AreEqual(0, BinaryPrimitives.ReadUInt16LittleEndian(context.Connection.LastWrite.AsSpan(7)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(context.Connection.LastWrite.AsSpan(9)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(context.Connection.LastWrite.AsSpan(11)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(context.Connection.LastWrite.AsSpan(13)));

        await context.Service.StopAsync(default).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MouseOutputSendDoesNotWaitForSerialWrite()
    {
        using TestContext context = CreateService(port: null);
        context.Discovery.Ports.Add("COM7");
        context.Connection.ConnectAllowed = true;
        await context.Service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => context.Service.IsConnected).ConfigureAwait(false);

        context.Connection.BlockWrites = true;
        await using IMouseOutput output = context.Service.CreateOutput();

        ValueTask send = output.SendAsync(
            new MouseInput(new(MouseButtons.Left, 1, 0, 0), DeviceName: null));

        Assert.IsTrue(send.IsCompletedSuccessfully);
        await WaitUntilAsync(() => context.Connection.WriteStarted).ConfigureAwait(false);

        context.Connection.ReleaseWrites();
        await context.Service.StopAsync(default).ConfigureAwait(false);
    }

    private static TestContext CreateService(string? port)
    {
        SteamInputBridgeSettings settings = new();
        settings.Teensy.Port = port;
        settings.Games["game"] = new GameProfile { Executable = @"C:\Game\game.exe" };
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(settings);
        SettingsService settingsService = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        TestPortDiscovery discovery = new();
        TestSerialConnection connection = new();
        TeensyMouseOutputService service = new(
            settingsService,
            discovery,
            connection,
            NullLogger<TeensyMouseOutputService>.Instance,
            TimeSpan.FromMilliseconds(10));
        return new(monitor, settingsService, discovery, connection, service);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private sealed class TestContext(
        TestOptionsMonitor<SteamInputBridgeSettings> monitor,
        SettingsService settings,
        TestPortDiscovery discovery,
        TestSerialConnection connection,
        TeensyMouseOutputService service) : IDisposable
    {
        public TestOptionsMonitor<SteamInputBridgeSettings> Monitor { get; } = monitor;

        public TestPortDiscovery Discovery { get; } = discovery;

        public TestSerialConnection Connection { get; } = connection;

        public TeensyMouseOutputService Service { get; } = service;

        public void Dispose()
        {
            Service.Dispose();
            Connection.Dispose();
            settings.Dispose();
        }
    }

    private sealed class TestPortDiscovery : TeensyPortDiscovery
    {
        public List<string> Ports { get; } = [];

        public override IReadOnlyList<string> GetCandidatePorts(string? configuredPort)
        {
            return TeensyPortDiscovery.OrderCandidatePorts(Ports, likelyTeensyPorts: [], configuredPort);
        }

        public override bool PortExists(string portName)
        {
            return Ports.Exists(port => string.Equals(port, portName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class TestSerialConnection : TeensySerialConnection
    {
        private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isConnected;
        private string? _portName;

        public bool ConnectAllowed { get; set; }

        public bool BlockWrites { get; set; }

        public bool WriteStarted => _writeStarted.Task.IsCompleted;

        public override bool IsConnected => _isConnected;

        public override string? PortName => _portName;

        public byte[] LastWrite { get; private set; } = [];

        public override bool TryConnect(IReadOnlyList<string> candidatePorts)
        {
            if (!ConnectAllowed || candidatePorts.Count == 0)
            {
                return false;
            }

            _isConnected = true;
            _portName = candidatePorts[0];
            return true;
        }

        public override bool TryWrite(byte[] frame, int bytes)
        {
            if (!IsConnected)
            {
                return false;
            }

            if (BlockWrites)
            {
                _ = _writeStarted.TrySetResult();
                _ = _releaseWrites.Task.Wait(TimeSpan.FromSeconds(3));
            }

            LastWrite = frame[..bytes];
            return true;
        }

        public void ReleaseWrites()
        {
            _ = _releaseWrites.TrySetResult();
        }

        public override void Close()
        {
            ReleaseWrites();
            _isConnected = false;
            _portName = null;
        }
    }
}
