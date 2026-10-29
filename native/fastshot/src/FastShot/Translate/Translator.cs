using System.Runtime.InteropServices;
using System.Text;
using FastShot.Core.Translate;

namespace FastShot;

public enum TranslateEngine { Offline, DeepL }

/// <summary>Picks the engine for a translation: the user's DeepL key when they chose DeepL and saved one, otherwise
/// Firefox's models on this PC.</summary>
public static class Translator
{
    public sealed record Result(string Text, string From, string Engine);

    public static bool UsesDeepL => Preferences.Shared.TranslateEngine == TranslateEngine.DeepL && DeepLKey.Value is not null;

    /// <param name="from">The source language, or null to detect it.</param>
    public static async Task<Result> Translate(string text, string? from, string to, Action<string>? status = null, CancellationToken cancellation = default)
    {
        if (UsesDeepL)
        {
            status?.Invoke("DeepL ile çevriliyor…");
            var (translated, detected) = await DeepL.Translate(text, from, to, DeepLKey.Value!, cancellation);
            return new Result(translated, detected ?? from ?? "", "DeepL");
        }
        var source = from ?? Languages.Guess(text) ?? "en";
        var result = await OfflineTranslator.Shared.Translate(text, source, to, status, cancellation);
        return new Result(result, source, "Çevrimdışı (Firefox modelleri)");
    }
}

/// <summary>The DeepL key, in Windows Credential Manager (encrypted for this Windows user), never in settings.json.</summary>
public static class DeepLKey
{
    const string Target = "FastShot:DeepL";
    const int Generic = 1, LocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public int Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int BlobSize;
        public IntPtr Blob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);

    public static string? Value
    {
        get
        {
            if (!CredRead(Target, Generic, 0, out var pointer)) return null;
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.BlobSize == 0) return null;
                var bytes = new byte[credential.BlobSize];
                Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally { CredFree(pointer); }
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                CredDelete(Target, Generic, 0);
                return;
            }
            var bytes = Encoding.Unicode.GetBytes(value.Trim());
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential
                {
                    Type = Generic, TargetName = Target, UserName = "DeepL API", BlobSize = bytes.Length, Blob = blob, Persist = LocalMachine,
                    Comment = "FastShot çevirisi için DeepL API anahtarı",
                };
                if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("DeepL anahtarı kaydedilemedi.");
            }
            finally { Marshal.FreeHGlobal(blob); }
        }
    }
}
