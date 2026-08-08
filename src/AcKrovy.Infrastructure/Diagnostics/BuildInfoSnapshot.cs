namespace AcKrovy.Infrastructure.Diagnostics;

/// <summary>Host-neutral, already-formatted build/runtime identity.</summary>
public sealed record BuildInfoSnapshot(
    string AssemblyLocation,
    string AssemblyName,
    string AssemblyVersion,
    string FileVersion,
    string ProductVersion,
    string InformationalVersion,
    string ModuleVersionId,
    string DllLengthBytes,
    string DllLastWriteTimeLocal,
    string DllLastWriteTimeUtc,
    string DllSha256,
    string ProcessName,
    string ProcessId,
    string ProcessStartTimeLocal,
    string RuntimeVersion,
    string FrameworkDescription,
    string OsDescription,
    string ProcessArchitecture,
    string GitHead,
    string GitBranch,
    string GitWorkingTreeDirty);
