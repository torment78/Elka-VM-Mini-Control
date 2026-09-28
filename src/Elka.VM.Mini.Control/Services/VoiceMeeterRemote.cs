using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Elka.VM.Mini.Control.Core;

namespace Elka.VM.Mini.Control.Services;

public sealed class VoiceMeeterRemote : IRemoteApi
{
    private IntPtr _library;
    private bool _loggedIn;
    private NoArgs? _login, _logout, _dirty;
    private GetVmType? _type;
    private GetFloat? _get;
    private GetString? _getString;
    private SetParameters? _set;
    public string? LibraryPath { get; private set; }

    public void Refresh()
    {
        if (_library == IntPtr.Zero) Load();
        if (!_loggedIn)
        {
            int code = _login!();
            if (code < 0) throw new InvalidOperationException($"VoiceMeeter API login failed ({code}). Close an unused API client and retry.");
            _loggedIn = true; // 1 is a valid login while VoiceMeeter is stopped.
        }
        // Keep the same login across VoiceMeeter restarts, as required by the SDK.
        if (_dirty!() < 0) throw new InvalidOperationException("Waiting for VoiceMeeter Potato. Open VoiceMeeter to connect.");
        if (_type!(out int type) != 0) throw new InvalidOperationException("Waiting for VoiceMeeter…");
        if (type != 3) throw new InvalidOperationException("VoiceMeeter Potato is required for eight SEL buses and submix levels.");
    }

    public float Read(string parameter)
    {
        int code = _get!(parameter, out float value);
        if (code != 0 || !float.IsFinite(value)) throw new InvalidOperationException($"Could not read {parameter} ({code}).");
        return value;
    }
    public void Write(string script)
    {
        int code = _set!(script);
        if (code != 0) throw new InvalidOperationException($"VoiceMeeter rejected the change ({code}).");
    }

    public string ReadText(string parameter)
    {
        var value = new StringBuilder(512);
        int code = _getString!(parameter, value);
        if (code != 0) throw new InvalidOperationException($"Could not read {parameter} ({code}).");
        return value.ToString();
    }

    private void Load()
    {
        string? path = InstalledFolders().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => Path.Combine(p, "VoicemeeterRemote64.dll")).FirstOrDefault(File.Exists);
        if (path is null) throw new InvalidOperationException("VoiceMeeter was not found in its installed location.");
        try
        {
            _library = NativeLibrary.Load(path);
            _login = Export<NoArgs>("VBVMR_Login"); _logout = Export<NoArgs>("VBVMR_Logout");
            _dirty = Export<NoArgs>("VBVMR_IsParametersDirty"); _type = Export<GetVmType>("VBVMR_GetVoicemeeterType");
            _get = Export<GetFloat>("VBVMR_GetParameterFloat"); _set = Export<SetParameters>("VBVMR_SetParameters");
            _getString = Export<GetString>("VBVMR_GetParameterStringW");
            LibraryPath = path;
        }
        catch
        {
            if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
            throw new InvalidOperationException("Could not load the installed VoiceMeeter API. Repair the VoiceMeeter installation.");
        }
    }

    private static IEnumerable<string> InstalledFolders()
    {
        var folders = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}");
                if (key?.GetValue("InstallLocation") is string location && Directory.Exists(location)) folders.Add(location);
                if (key?.GetValue("UninstallString") is string uninstall)
                {
                    string exe = uninstall.Trim();
                    if (exe.StartsWith('"')) exe = exe.Split('"')[1];
                    else if (exe.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) is int end && end >= 0) exe = exe[..(end + 4)];
                    if (Path.GetDirectoryName(exe) is string folder && Directory.Exists(folder)) folders.Add(folder);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter"));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VB", "Voicemeeter"));
        return folders;
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Dispose()
    {
        if (_loggedIn) { _logout!(); _loggedIn = false; }
        if (_library != IntPtr.Zero) { NativeLibrary.Free(_library); _library = IntPtr.Zero; }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetVmType(out int type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int GetFloat(string parameter, out float value);
    // The W API returns UTF-16, but its parameter name remains ANSI.
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int GetString([MarshalAs(UnmanagedType.LPStr)] string parameter, [Out] StringBuilder value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int SetParameters(string script);
}
