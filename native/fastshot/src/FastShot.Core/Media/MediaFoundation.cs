using System.Runtime.InteropServices;

namespace FastShot.Core.Media;

/// <summary>The slice of Media Foundation FastShot needs: a sink writer to encode MP4 and a
/// source reader to decode it. Raw COM interop, no third-party wrappers.</summary>
internal static class MF
{
    const uint Version = 0x00020070;
    static bool started;
    static readonly object gate = new();

    public static void EnsureStarted()
    {
        lock (gate)
        {
            if (started) return;
            Check(MFStartup(Version, 0), "MFStartup");
            started = true;
        }
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new MediaException($"{what} başarısız oldu (0x{hr:X8}).", hr);
    }

    [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);
    [DllImport("mfreadwrite.dll")] public static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);
    [DllImport("mfreadwrite.dll")] public static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IMFAttributes? attributes, out IMFSourceReader reader);

    public static Guid Guid(string value) => new(value);

    // Attribute keys and types.
    public static Guid MF_MT_MAJOR_TYPE = Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static Guid MF_MT_SUBTYPE = Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static Guid MF_MT_FRAME_SIZE = Guid("1652c33d-d6b2-4012-b834-72030849a37d");
    public static Guid MF_MT_FRAME_RATE = Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static Guid MF_MT_PIXEL_ASPECT_RATIO = Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static Guid MF_MT_INTERLACE_MODE = Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static Guid MF_MT_AVG_BITRATE = Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static Guid MF_MT_DEFAULT_STRIDE = Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static Guid MF_MT_MINIMUM_DISPLAY_APERTURE = Guid("d7388766-18fe-48c6-a177-ee894867c8c4");
    public static Guid MF_MT_ALL_SAMPLES_INDEPENDENT = Guid("c9173739-5e56-461c-b713-46fb995cb95f");
    public static Guid MF_MT_AUDIO_NUM_CHANNELS = Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static Guid MFMediaType_Video = Guid("73646976-0000-0010-8000-00AA00389B71");
    public static Guid MFMediaType_Audio = Guid("73647561-0000-0010-8000-00AA00389B71");
    public static Guid MFVideoFormat_H264 = Guid("34363248-0000-0010-8000-00AA00389B71");
    public static Guid MFVideoFormat_HEVC = Guid("43564548-0000-0010-8000-00AA00389B71");
    public static Guid MFVideoFormat_NV12 = Guid("3231564E-0000-0010-8000-00AA00389B71");
    public static Guid MFAudioFormat_PCM = Guid("00000001-0000-0010-8000-00AA00389B71");
    public static Guid MFAudioFormat_AAC = Guid("00001610-0000-0010-8000-00AA00389B71");
    public static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = Guid("a634a91c-822b-41b9-a494-4de4643612b0");
    public static Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static Guid MF_SINK_WRITER_DISABLE_THROTTLING = Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static Guid MF_PD_DURATION = Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    public static Guid GUID_NULL = System.Guid.Empty;
    public static Guid MF_MT_YUV_MATRIX = Guid("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static Guid MF_MT_VIDEO_NOMINAL_RANGE = Guid("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static Guid MF_MT_VIDEO_PRIMARIES = Guid("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
    public static Guid MF_MT_TRANSFER_FUNCTION = Guid("5fb0fce9-be5c-4935-a811-ec838f8eed93");
    public static Guid IID_IMF2DBuffer = Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB");
    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;
    public const uint MF_SOURCE_READER_ANY_STREAM = 0xFFFFFFFE;
    public const uint MFVideoTransferMatrix_BT709 = 1;
    public const uint MFNominalRange_16_235 = 2;
    public const uint MFVideoPrimaries_BT709 = 2;
    public const uint MFVideoTransFunc_709 = 5;

    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
    public const uint MF_SOURCE_READER_MEDIASOURCE = 0xFFFFFFFF;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
    public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;
    public const uint MFVideoInterlace_Progressive = 2;

    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
    public static (uint high, uint low) Unpack(ulong value) => ((uint)(value >> 32), (uint)(value & 0xFFFFFFFF));

    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}

public sealed class MediaException(string message, int hresult) : Exception(message)
{
    public int HResult2 { get; } = hresult;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort vt;
    public ushort reserved1, reserved2, reserved3;
    public long value;
    public IntPtr pointer2;

    public const ushort VT_I8 = 20;
    public const ushort VT_UI8 = 21;

    public static PropVariant Int64(long v) => new() { vt = VT_I8, value = v };
}

/// <summary>MFVideoArea, the blob behind MF_MT_MINIMUM_DISPLAY_APERTURE.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VideoArea
{
    public ushort OffsetXFraction;
    public short OffsetX;
    public ushort OffsetYFraction;
    public short OffsetY;
    public int Width;
    public int Height;
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
    [PreserveSig] int GetItemType([In] ref Guid key, out int type);
    [PreserveSig] int CompareItem([In] ref Guid key, [In] ref PropVariant value, out int result);
    [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32([In] ref Guid key, out uint value);
    [PreserveSig] int GetUINT64([In] ref Guid key, out ulong value);
    [PreserveSig] int GetDouble([In] ref Guid key, out double value);
    [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
    [PreserveSig] int GetStringLength([In] ref Guid key, out uint length);
    [PreserveSig] int GetString([In] ref Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize([In] ref Guid key, out uint size);
    [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
    [PreserveSig] int SetItem([In] ref Guid key, [In] ref PropVariant value);
    [PreserveSig] int DeleteItem([In] ref Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32([In] ref Guid key, uint value);
    [PreserveSig] int SetUINT64([In] ref Guid key, ulong value);
    [PreserveSig] int SetDouble([In] ref Guid key, double value);
    [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
    [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    // IMFAttributes
    [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
    [PreserveSig] int GetItemType([In] ref Guid key, out int type);
    [PreserveSig] int CompareItem([In] ref Guid key, [In] ref PropVariant value, out int result);
    [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32([In] ref Guid key, out uint value);
    [PreserveSig] int GetUINT64([In] ref Guid key, out ulong value);
    [PreserveSig] int GetDouble([In] ref Guid key, out double value);
    [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
    [PreserveSig] int GetStringLength([In] ref Guid key, out uint length);
    [PreserveSig] int GetString([In] ref Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize([In] ref Guid key, out uint size);
    [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
    [PreserveSig] int SetItem([In] ref Guid key, [In] ref PropVariant value);
    [PreserveSig] int DeleteItem([In] ref Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32([In] ref Guid key, uint value);
    [PreserveSig] int SetUINT64([In] ref Guid key, ulong value);
    [PreserveSig] int SetDouble([In] ref Guid key, double value);
    [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
    [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
    // IMFMediaType
    [PreserveSig] int GetMajorType(out Guid majorType);
    [PreserveSig] int IsCompressedFormat(out int compressed);
    [PreserveSig] int IsEqual(IMFMediaType other, out uint flags);
    [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
    [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // IMFAttributes
    [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
    [PreserveSig] int GetItemType([In] ref Guid key, out int type);
    [PreserveSig] int CompareItem([In] ref Guid key, [In] ref PropVariant value, out int result);
    [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
    [PreserveSig] int GetUINT32([In] ref Guid key, out uint value);
    [PreserveSig] int GetUINT64([In] ref Guid key, out ulong value);
    [PreserveSig] int GetDouble([In] ref Guid key, out double value);
    [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
    [PreserveSig] int GetStringLength([In] ref Guid key, out uint length);
    [PreserveSig] int GetString([In] ref Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize([In] ref Guid key, out uint size);
    [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
    [PreserveSig] int SetItem([In] ref Guid key, [In] ref PropVariant value);
    [PreserveSig] int DeleteItem([In] ref Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32([In] ref Guid key, uint value);
    [PreserveSig] int SetUINT64([In] ref Guid key, ulong value);
    [PreserveSig] int SetDouble([In] ref Guid key, double value);
    [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
    [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr unknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
    // IMFSample
    [PreserveSig] int GetSampleFlags(out uint flags);
    [PreserveSig] int SetSampleFlags(uint flags);
    [PreserveSig] int GetSampleTime(out long time);
    [PreserveSig] int SetSampleTime(long time);
    [PreserveSig] int GetSampleDuration(out long duration);
    [PreserveSig] int SetSampleDuration(long duration);
    [PreserveSig] int GetBufferCount(out uint count);
    [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    [PreserveSig] int RemoveBufferByIndex(uint index);
    [PreserveSig] int RemoveAllBuffers();
    [PreserveSig] int GetTotalLength(out uint length);
    [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out uint length);
    [PreserveSig] int SetCurrentLength(uint length);
    [PreserveSig] int GetMaxLength(out uint length);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriter
{
    [PreserveSig] int AddStream(IMFMediaType targetType, out uint streamIndex);
    [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputType, IMFAttributes? encodingParameters);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
    [PreserveSig] int SendStreamTick(uint streamIndex, long timestamp);
    [PreserveSig] int PlaceMarker(uint streamIndex, IntPtr context);
    [PreserveSig] int NotifyEndOfSegment(uint streamIndex);
    [PreserveSig] int Flush(uint streamIndex);
    [PreserveSig] int Finalize();
    [PreserveSig] int GetServiceForStream(uint streamIndex, [In] ref Guid service, [In] ref Guid iid, out IntPtr value);
    [PreserveSig] int GetStatistics(uint streamIndex, IntPtr statistics);
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(uint streamIndex, out int selected);
    [PreserveSig] int SetStreamSelection(uint streamIndex, int selected);
    [PreserveSig] int GetNativeMediaType(uint streamIndex, uint typeIndex, out IMFMediaType type);
    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType type);
    [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType type);
    [PreserveSig] int SetCurrentPosition([In] ref Guid timeFormat, [In] ref PropVariant position);
    [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out IMFSample? sample);
    [PreserveSig] int Flush(uint streamIndex);
    [PreserveSig] int GetServiceForStream(uint streamIndex, [In] ref Guid service, [In] ref Guid iid, out IntPtr value);
    [PreserveSig] int GetPresentationAttribute(uint streamIndex, [In] ref Guid attribute, out PropVariant value);
}

[ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMF2DBuffer
{
    [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
    [PreserveSig] int Unlock2D();
    [PreserveSig] int GetScanline0AndPitch(out IntPtr scanline0, out int pitch);
    [PreserveSig] int IsContiguousFormat(out int contiguous);
    [PreserveSig] int GetContiguousLength(out uint length);
    [PreserveSig] int ContiguousCopyTo(IntPtr destination, uint length);
    [PreserveSig] int ContiguousCopyFrom(IntPtr source, uint length);
}
