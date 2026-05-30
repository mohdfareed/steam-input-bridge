using System;
using System.ComponentModel;
using System.Diagnostics;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace SteamInputBridge.Runtime;

internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeHJOB _job;

    // MARK: Publics
    // ========================================================================

    public static WindowsProcessJob Own(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        SafeHJOB job = SafeHJOB.CreateObject(null, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception("Could not create process job.");
        }

        try
        {
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION information = new()
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };

            job.SetInformationObject(
                JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                in information);

            if (!job.AssignProcessToObject((HPROCESS)process.Handle))
            {
                throw new Win32Exception("Could not assign process to job.");
            }

            SafeHJOB owned = job;
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

    private WindowsProcessJob(SafeHJOB job)
    {
        _job = job;
    }
}
