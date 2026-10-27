using System.Runtime.InteropServices;

namespace Radio.Windows.Native;

internal static class MediaEngineInterop
{
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetUnknown(IntPtr attributes, ref Guid key, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetGuid(IntPtr attributes, ref Guid key, ref Guid value);

    internal static IMFMediaEngine CreateAudioEngine(MediaEngineNotify notify)
    {
        Marshal.ThrowExceptionForHR(MFCreateAttributes(out var attributes, 2));
        IntPtr callback = IntPtr.Zero;
        object? factoryObject = null;
        try
        {
            callback = Marshal.GetIUnknownForObject(notify);
            // IMFAttributes::SetUnknown is slot 27 (IUnknown slots included), per mfobjects.h.
            var vtable = Marshal.ReadIntPtr(attributes);
            var setUnknown = Marshal.GetDelegateForFunctionPointer<SetUnknown>(Marshal.ReadIntPtr(vtable, 27 * IntPtr.Size));
            var callbackKey = new Guid("c60381b8-83a4-41f8-a3d0-de05076849a9");
            Marshal.ThrowExceptionForHR(setUnknown(attributes, ref callbackKey, callback));
            var setGuid = Marshal.GetDelegateForFunctionPointer<SetGuid>(Marshal.ReadIntPtr(vtable, 24 * IntPtr.Size));
            var compatibilityKey = new Guid("3ef26ad4-dc54-45de-b9af-76c8c66bfa8e");
            var windows10Mode = new Guid("5b25e089-6ca7-4139-a2cb-fcaab39552a3");
            Marshal.ThrowExceptionForHR(setGuid(attributes, ref compatibilityKey, ref windows10Mode));
            var clsid = new Guid("b44392da-499b-446b-a4cb-005fead0e6d5");
            factoryObject = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)!);
            // MF_MEDIA_ENGINE_AUDIOONLY | MF_MEDIA_ENGINE_DISABLE_LOCAL_PLUGINS.
            ((IMFMediaEngineClassFactory)factoryObject!).CreateInstance(0x11, attributes, out var engine);
            return engine;
        }
        finally
        {
            if (factoryObject is not null) Marshal.ReleaseComObject(factoryObject);
            if (callback != IntPtr.Zero) Marshal.Release(callback);
            Marshal.Release(attributes);
        }
    }

}

[ComVisible(true), Guid("fee7c112-e776-42b5-9bbf-0048524e2bd5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMediaEngineNotify
{
    [PreserveSig] int EventNotify(uint eventId, UIntPtr parameter1, uint parameter2);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class MediaEngineNotify(Action<uint> callback) : IMediaEngineNotify
{
    public int EventNotify(uint eventId, UIntPtr parameter1, uint parameter2)
    {
        try { callback(eventId); }
        catch (InvalidOperationException) { /* The UI message loop has already closed. */ }
        return 0;
    }
}

[ComImport, Guid("4d645ace-26aa-4688-9be1-df3516990b93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaEngineClassFactory
{
    void CreateInstance(uint flags, IntPtr attributes, out IMFMediaEngine engine);
}

[ComImport, Guid("fc0e10d2-ab2a-4501-a951-06bb1075184c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaError
{
    [PreserveSig] ushort GetErrorCode();
    [PreserveSig] int GetExtendedErrorCode();
}

// Keep the SDK method order exactly: scalar return values are PreserveSig; HRESULT methods are not.
[ComImport, Guid("98a1b0bb-03eb-4935-ae7c-93c1fa0e1c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaEngine
{
    void GetError(out IMFMediaError error);
    void SetErrorCode(int error);
    void SetSourceElements(IntPtr elements);
    void SetSource([MarshalAs(UnmanagedType.BStr)] string url);
    void GetCurrentSource([MarshalAs(UnmanagedType.BStr)] out string url);
    [PreserveSig] ushort GetNetworkState();
    [PreserveSig] int GetPreload();
    void SetPreload(int preload);
    void GetBuffered(out IntPtr ranges);
    void Load();
    void CanPlayType([MarshalAs(UnmanagedType.BStr)] string type, out int answer);
    [PreserveSig] ushort GetReadyState();
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool IsSeeking();
    [PreserveSig] double GetCurrentTime();
    void SetCurrentTime(double time);
    [PreserveSig] double GetStartTime();
    [PreserveSig] double GetDuration();
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool IsPaused();
    [PreserveSig] double GetDefaultPlaybackRate();
    void SetDefaultPlaybackRate(double rate);
    [PreserveSig] double GetPlaybackRate();
    void SetPlaybackRate(double rate);
    void GetPlayed(out IntPtr ranges);
    void GetSeekable(out IntPtr ranges);
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool IsEnded();
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool GetAutoPlay();
    void SetAutoPlay([MarshalAs(UnmanagedType.Bool)] bool value);
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool GetLoop();
    void SetLoop([MarshalAs(UnmanagedType.Bool)] bool value);
    void Play();
    void Pause();
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool GetMuted();
    void SetMuted([MarshalAs(UnmanagedType.Bool)] bool value);
    [PreserveSig] double GetVolume();
    void SetVolume(double value);
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool HasVideo();
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool HasAudio();
    void GetNativeVideoSize(out uint width, out uint height);
    void GetVideoAspectRatio(out uint width, out uint height);
    void Shutdown();
}
