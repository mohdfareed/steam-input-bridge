using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VirtualMouse.Runtime;

internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle _job;

    // MARK: Publics
    // ========================================================================

    public static WindowsProcessJob Own(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        SafeFileHandle job = CreateJobObject(nint.Zero, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create process job.");
        }

        try
        {
            JobObjectExtendedLimitInformation information = new()
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };

            int length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            nint buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(information, buffer, fDeleteOld: false);
                if (!SetInformationJobObject(
                    job,
                    JobObjectInfoClass.ExtendedLimitInformation,
                    buffer,
                    (uint)length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure process job.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not assign process to job.");
            }

            SafeFileHandle owned = job;
            job = null!;
            return new WindowsProcessJob(owned);
        }
        finally
        {
            job?.Dispose();
        }
    }

    public void Dispose()
    {
        _job.Dispose();
    }

    // MARK: Privates
    // ========================================================================

    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private WindowsProcessJob(SafeFileHandle job)
    {
        _job = job;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        JobObjectInfoClass infoClass,
        nint information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
