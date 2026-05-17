using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Inputs;

namespace Hosting;

internal sealed class GamepadReportPipeServer(
    Guid sessionId,
    Action<GamepadState> handleReport,
    Action<Guid> handleClosed) : IAsyncDisposable
{
    private const string PipePrefix = "Hosting.gamepad.";
    private readonly CancellationTokenSource _cancellation = new();
    private readonly NamedPipeServerStream _pipe = CreatePipe(GetPipeName(sessionId));
    private Task? _runTask;
    private int _disposed;

    public string PipeName => GetPipeName(sessionId);

    public void Start()
    {
        _runTask = Task.Run(Run, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);
        _pipe.Dispose();

        if (_runTask is not null && _runTask.Id != Task.CurrentId)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (IOException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        _cancellation.Dispose();
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static string GetPipeName(Guid sessionId)
    {
        return PipePrefix + sessionId.ToString("N");
    }

    private async Task Run()
    {
        try
        {
            await _pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
            byte[] buffer = new byte[GamepadReportPipeFrame.Size];

            while (!_cancellation.IsCancellationRequested)
            {
                if (!await ReadFrameAsync(buffer, _cancellation.Token).ConfigureAwait(false))
                {
                    return;
                }

                handleReport(GamepadReportPipeFrame.Read(buffer));
            }
        }
        finally
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _cancellation.Dispose();
                handleClosed(sessionId);
            }
        }
    }

    private async Task<bool> ReadFrameAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await _pipe
                .ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }
}

internal readonly record struct GamepadReportSessionInfo(Guid SessionId, string PipeName);

/// <summary>Sends gamepad reports to an attached host session.</summary>
public sealed class GamepadReportClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly byte[] _buffer = new byte[GamepadReportPipeFrame.Size];

    private GamepadReportClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    internal static async Task<GamepadReportClient> ConnectAsync(
        GamepadReportSessionInfo session,
        CancellationToken cancellationToken = default)
    {
        NamedPipeClientStream pipe = new(
            ".",
            session.PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new GamepadReportClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends one gamepad report.</summary>
    public void Send(in GamepadInput input)
    {
        GamepadReportPipeFrame.Write(_buffer, input.State);
        _pipe.Write(_buffer, 0, _buffer.Length);
    }

    /// <summary>Sends one gamepad report.</summary>
    public ValueTask SendAsync(in GamepadInput input, CancellationToken cancellationToken = default)
    {
        GamepadReportPipeFrame.Write(_buffer, input.State);
        return _pipe.WriteAsync(_buffer.AsMemory(0, _buffer.Length), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pipe.Dispose();
    }
}

internal static class GamepadReportPipeFrame
{
    public const int Size = 44;
    private const int ButtonsOffset = 0;
    private const int LeftXOffset = 4;
    private const int LeftYOffset = 6;
    private const int RightXOffset = 8;
    private const int RightYOffset = 10;
    private const int LeftTriggerOffset = 12;
    private const int RightTriggerOffset = 14;
    private const int MotionFlagsOffset = 16;
    private const int GyroXOffset = 20;
    private const int GyroYOffset = 24;
    private const int GyroZOffset = 28;
    private const int AccelXOffset = 32;
    private const int AccelYOffset = 36;
    private const int AccelZOffset = 40;
    private const byte HasGyroFlag = 1;
    private const byte HasAccelerometerFlag = 2;

    public static void Write(Span<byte> buffer, GamepadState state)
    {
        buffer.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(buffer[ButtonsOffset..], (int)state.Buttons);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[LeftXOffset..], state.LeftX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[LeftYOffset..], state.LeftY);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[RightXOffset..], state.RightX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[RightYOffset..], state.RightY);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[LeftTriggerOffset..], state.LeftTrigger);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[RightTriggerOffset..], state.RightTrigger);
        buffer[MotionFlagsOffset] = GetMotionFlags(state.Motion);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[GyroXOffset..], state.Motion.GyroX);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[GyroYOffset..], state.Motion.GyroY);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[GyroZOffset..], state.Motion.GyroZ);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[AccelXOffset..], state.Motion.AccelX);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[AccelYOffset..], state.Motion.AccelY);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[AccelZOffset..], state.Motion.AccelZ);
    }

    public static GamepadState Read(ReadOnlySpan<byte> buffer)
    {
        byte motionFlags = buffer[MotionFlagsOffset];
        GamepadMotion motion = new(
            (motionFlags & HasGyroFlag) != 0,
            BinaryPrimitives.ReadSingleLittleEndian(buffer[GyroXOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[GyroYOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[GyroZOffset..]),
            (motionFlags & HasAccelerometerFlag) != 0,
            BinaryPrimitives.ReadSingleLittleEndian(buffer[AccelXOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[AccelYOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[AccelZOffset..]));

        return new GamepadState(
            (GamepadButtons)BinaryPrimitives.ReadInt32LittleEndian(buffer[ButtonsOffset..]),
            BinaryPrimitives.ReadInt16LittleEndian(buffer[LeftXOffset..]),
            BinaryPrimitives.ReadInt16LittleEndian(buffer[LeftYOffset..]),
            BinaryPrimitives.ReadInt16LittleEndian(buffer[RightXOffset..]),
            BinaryPrimitives.ReadInt16LittleEndian(buffer[RightYOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(buffer[LeftTriggerOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(buffer[RightTriggerOffset..]),
            motion);
    }

    private static byte GetMotionFlags(GamepadMotion motion)
    {
        byte flags = 0;
        if (motion.HasGyro)
        {
            flags |= HasGyroFlag;
        }

        if (motion.HasAccelerometer)
        {
            flags |= HasAccelerometerFlag;
        }

        return flags;
    }
}
