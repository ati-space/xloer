using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FastShot.Core;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureKind>))]
public enum CaptureKind { image, video }

/// <summary>A capture plus its annotations, kept separate so it can be reopened and edited at any time.
/// The original media is never modified.</summary>
public sealed class CaptureProject
{
    [JsonPropertyName("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("kind")] public CaptureKind Kind { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("modifiedAt")] public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Canvas size in points; the media's pixel size divided by <see cref="PixelScale"/>.</summary>
    [JsonPropertyName("canvasSize")] public Size CanvasSize { get; set; }
    [JsonPropertyName("pixelScale")] public double PixelScale { get; set; } = 1;
    [JsonPropertyName("mediaFile")] public string MediaFile { get; set; } = "";
    [JsonPropertyName("annotations")] public List<Annotation> Annotations { get; set; } = [];
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("trimStart")] public double? TrimStart { get; set; }
    [JsonPropertyName("trimEnd")] public double? TrimEnd { get; set; }

    public CaptureProject() { }

    public CaptureProject(CaptureKind kind, Size canvasSize, double pixelScale, string mediaFile)
    {
        Kind = kind;
        CanvasSize = canvasSize;
        PixelScale = pixelScale;
        MediaFile = mediaFile;
    }

    /// <summary>The part of a recording that gets exported, or null for all of it.</summary>
    [JsonIgnore]
    public (double start, double end)? TrimRange
    {
        get
        {
            if (Duration is not { } duration || duration <= 0) return null;
            var start = Math.Max(0, Math.Min(TrimStart ?? 0, duration));
            var end = Math.Max(start, Math.Min(TrimEnd ?? duration, duration));
            return end - start > 0.05 ? (start, end) : null;
        }
    }

    [JsonIgnore] public int PixelWidth => (int)Math.Round(CanvasSize.Width * PixelScale);
    [JsonIgnore] public int PixelHeight => (int)Math.Round(CanvasSize.Height * PixelScale);
}

public sealed class ProjectLibrary
{
    public const string PackageExtension = "fastshot";
    const string ManifestName = "project.json";
    const string ThumbnailName = "thumbnail.jpg";

    public string Root { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastShot", "Library");

    public ProjectLibrary(string? root = null)
    {
        Root = root ?? DefaultRoot;
        Directory.CreateDirectory(Root);
    }

    public string Folder(Guid id) => Path.Combine(Root, id.ToString("D").ToUpperInvariant());
    public string MediaPath(CaptureProject project) => Path.Combine(Folder(project.Id), project.MediaFile);
    public string ThumbnailPath(CaptureProject project) => Path.Combine(Folder(project.Id), ThumbnailName);

    /// <summary>Creates the folder for a new capture; the caller writes the media to <see cref="MediaPath"/>.</summary>
    public CaptureProject MakeProject(CaptureKind kind, Size canvasSize, double pixelScale, string mediaExtension)
    {
        var project = new CaptureProject(kind, canvasSize, pixelScale, $"original.{mediaExtension}");
        Directory.CreateDirectory(Folder(project.Id));
        return project;
    }

    public void Save(CaptureProject project)
    {
        project.ModifiedAt = DateTime.UtcNow;
        var folder = Folder(project.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ManifestName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(project, ProjectJson.Options));
        File.Move(temp, path, overwrite: true);
    }

    public CaptureProject? Project(Guid id)
    {
        try { return Read(Folder(id)); } catch { return null; }
    }

    public List<CaptureProject> AllProjects()
    {
        var projects = new List<CaptureProject>();
        if (!Directory.Exists(Root)) return projects;
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var project = Read(folder);
                if (File.Exists(MediaPath(project))) projects.Add(project);
            }
            catch { /* not a project folder */ }
        }
        return projects.OrderByDescending(p => p.CreatedAt).ToList();
    }

    public void Delete(CaptureProject project)
    {
        var folder = Folder(project.Id);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    public void WriteThumbnail(BitmapSource image, CaptureProject project)
    {
        try
        {
            var small = ImageExporter.Scaled(image, 520);
            var data = ImageExporter.Encode(small, ImageFormat.Jpeg, 0.8);
            File.WriteAllBytes(ThumbnailPath(project), data);
        }
        catch { /* a missing thumbnail only costs a preview */ }
    }

    /// <summary>Copies a capture out as a self-contained .fastshot package that stays editable.</summary>
    public void ExportPackage(CaptureProject project, string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        if (File.Exists(path)) File.Delete(path);
        CopyDirectory(Folder(project.Id), path);
    }

    /// <summary>Brings a .fastshot package into the library, as a copy if its id is already taken.</summary>
    public CaptureProject ImportPackage(string path)
    {
        var project = Read(path);
        if (Directory.Exists(Folder(project.Id)))
        {
            if (string.Equals(Path.GetFullPath(Folder(project.Id)).TrimEnd('\\'), Path.GetFullPath(path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return project;
            project.Id = Guid.NewGuid();
        }
        CopyDirectory(path, Folder(project.Id));
        Save(project);
        return project;
    }

    /// <summary>Wraps an existing image or movie file in a new project.</summary>
    public CaptureProject ImportMedia(string path, CaptureKind kind, Size canvasSize, double pixelScale, double? duration = null)
    {
        var project = MakeProject(kind, canvasSize, pixelScale, Path.GetExtension(path).TrimStart('.').ToLowerInvariant());
        project.Duration = duration;
        File.Copy(path, MediaPath(project), overwrite: true);
        Save(project);
        return project;
    }

    static CaptureProject Read(string folder)
    {
        var json = File.ReadAllText(Path.Combine(folder, ManifestName));
        return JsonSerializer.Deserialize<CaptureProject>(json, ProjectJson.Options) ?? throw new InvalidDataException("Proje okunamadı.");
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
