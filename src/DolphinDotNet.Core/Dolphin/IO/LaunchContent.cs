using System;
using System.IO;
using Dolphin.Storage;
using StorageApi = Dolphin.Storage.Storage;

namespace Dolphin.IO;

public enum LaunchContentFailure
{
    None, MissingLaunchPath, InvalidPath, UnsupportedDevice, AmbiguousSource,
    UnsupportedImage, MountFailed, ContentNotFound, ValidationRejected,
    MountChanged, CleanupFailed
}

public enum LaunchContentImageMode { ContainingDirectory, MountImage }
public enum LaunchContentDvdSource { Automatic, Disc, GcLoader }

/// <summary>An explicit fallback mount and a directory relative to its root.
/// No devices or directories are searched unless the caller supplies them.</summary>
public sealed class LaunchContentLocation
{
    public MountConfiguration Configuration { get; }
    public string Directory { get; }

    public LaunchContentLocation(MountConfiguration configuration, string directory = "")
    {
        if (configuration == null) throw new ArgumentNullException("configuration");
        if (!LaunchContentPaths.ValidRelative(directory, true))
            throw new ArgumentException("Use a relative content directory without dot components.", "directory");
        Configuration = configuration;
        Directory = directory.Length == 0 || directory.EndsWith("/", StringComparison.Ordinal)
            ? directory : directory + "/";
        if (!LaunchContentPaths.Fits(StorageApi.Root(configuration.Device) + Directory))
            throw new ArgumentException("Content directory is too long.", "directory");
    }
}

public sealed class LaunchContentOptions
{
    /// <summary>At most 16 ordered locations, used only without a usable source
    /// hint. A recognized source's missing or rejected content is authoritative.</summary>
    public LaunchContentLocation[] Fallbacks { get; set; } = new LaunchContentLocation[0];
    /// <summary>Verify application identity using the candidate's OpenRead.
    /// Close all streams before returning. Exceptions propagate after cleanup.
    /// Do not change mount lifecycles from this callback.</summary>
    public Func<LaunchContent, bool>? Validate { get; set; }
    /// <summary>Choose explicitly whether ISO/GCM arguments identify sibling
    /// files or files inside the image. No compressed-image conversion occurs.</summary>
    public LaunchContentImageMode ImageMode { get; set; }
    /// <summary>Swiss uses dvd:/ for several devices. Automatic requires a
    /// validator and will not reset a caller's mounted physical/ODE disc.</summary>
    public LaunchContentDvdSource DvdSource { get; set; }
}

public sealed class LaunchContentAttempt
{
    public string SourcePath { get; }
    public string RootPath { get; }
    public StorageDevice Device { get; }
    public LaunchContentFailure Failure { get; }
    public MountFailure MountFailure { get; }

    internal LaunchContentAttempt(string source, string root, StorageDevice device,
        LaunchContentFailure failure, MountFailure mountFailure)
    { SourcePath = source; RootPath = root; Device = device; Failure = failure; MountFailure = mountFailure; }
}

public sealed class LaunchContentResult
{
    public LaunchContent? Content { get; }
    public LaunchContentFailure Failure { get; }
    public bool Succeeded => Content != null && Failure == LaunchContentFailure.None;
    public LaunchContentAttempt[] Attempts { get; }

    internal LaunchContentResult(LaunchContent? content, LaunchContentFailure failure, LaunchContentAttempt[] attempts)
    { Content = content; Failure = failure; Attempts = attempts; }
}

/// <summary>A content directory bound to one filesystem generation. Mount and
/// Dispose on the application executor, with all mount lifecycle changes
/// serialized. Close streams before Dispose; only mounts acquired here are
/// released. Existing mounts and replacement generations are never owned.</summary>
public sealed class LaunchContent : IDisposable
{
    private readonly string directory;
    private readonly LaunchContentMounts owned;
    private readonly MountedFileSystem? source;
    private bool disposed;
    public MountedFileSystem FileSystem { get; }
    public StorageDevice Device => FileSystem.Device;
    public string RootPath { get; }
    public string LaunchPath { get; }
    public string SourcePath { get; }
    public bool IsFallback { get; }
    public bool IsValid => !disposed && FileSystem.IsValid && (source == null || source.IsValid);

    internal LaunchContent(MountedFileSystem fileSystem, MountedFileSystem? sourceFileSystem,
        string relativeDirectory, string launchPath, string sourcePath, bool fallback, LaunchContentMounts mounts)
    {
        FileSystem = fileSystem; source = sourceFileSystem; directory = relativeDirectory;
        RootPath = fileSystem.RootPath + directory; LaunchPath = launchPath;
        SourcePath = sourcePath; IsFallback = fallback; owned = mounts;
    }

    private string Child(string relativePath)
    {
        if (disposed) throw new ObjectDisposedException("LaunchContent");
        if (!IsValid) throw new DriveNotFoundException("The content filesystem mount is no longer active.");
        if (!LaunchContentPaths.ValidRelative(relativePath, false) || !LaunchContentPaths.Fits(RootPath + relativePath))
            throw new ArgumentException("Use a relative file path within the content directory.", "relativePath");
        string joined = directory + relativePath;
        if (!LaunchContentPaths.ValidRelative(joined, false))
            throw new ArgumentException("The combined content path exceeds filesystem limits.", "relativePath");
        return joined;
    }

    /// <summary>Build a path in the current namespace. A path string cannot
    /// retain generation checks; prefer OpenRead when lifetime matters.</summary>
    public string GetPath(string relativePath) => FileSystem.GetPath(Child(relativePath));
    public FileStream OpenRead(string relativePath)
        => FileSystem.Open(Child(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);

    public void Dispose()
    {
        if (disposed) return;
        owned.Release();
        disposed = true;
    }

    public static LaunchContentResult Mount(LaunchContentOptions? options = null)
        => Mount(LaunchContext.ExecutablePath, options);

    /// <summary>Resolve a launcher-supplied source path, or an explicit override.
    /// This does not change the working directory, select a hardware disc image,
    /// or expose a host computer directory to an emulated GameCube.</summary>
    public static LaunchContentResult Mount(string launchPath, LaunchContentOptions? options = null)
    {
        if (launchPath == null) throw new ArgumentNullException("launchPath");
        return new LaunchContentResolver(launchPath, options ?? new LaunchContentOptions()).Run();
    }
}

// Mount acquisition and rollback are startup operations. The application must
// serialize lifecycle changes, just as it must keep validation callbacks from
// remounting devices. Generation checks also protect later stale disposal.
internal sealed class LaunchContentMounts
{
    private readonly MountedFileSystem?[] mounts = new MountedFileSystem?[2];
    private int count;

    internal MountResult Acquire(MountConfiguration configuration)
    {
        bool borrowed = StorageApi.IsMounted(configuration.Device);
        var result = StorageApi.Mount(configuration);
        if (result.Succeeded && !borrowed) mounts[count++] = result.FileSystem!;
        return result;
    }

    internal void Release()
    {
        while (count != 0)
        {
            var mount = mounts[count - 1]!;
            if (mount.IsValid) StorageApi.Unmount(mount.Device);
            mounts[--count] = null;
        }
    }
}

internal static class LaunchContentPaths
{
    // Match storage.c: 1024 UTF-8 bytes including the terminator, at most
    // 64 components, and at most 255 UTF-8 bytes in any one component.
    internal static bool Fits(string value)
    {
        if (value == null || value.Length > 1023) return false;
        int bytes = 0;
        for (int i = 0; i < value.Length; i++)
        {
            int c = value[i];
            if (c < 0x80) bytes++;
            else if (c < 0x800) bytes += 2;
            else if (c >= 0xd800 && c <= 0xdbff)
            {
                if (++i == value.Length || value[i] < 0xdc00 || value[i] > 0xdfff) return false;
                bytes += 4;
            }
            else if (c >= 0xdc00 && c <= 0xdfff) return false;
            else bytes += 3;
            if (bytes > 1023) return false;
        }
        return true;
    }

    internal static bool ValidRelative(string value, bool directory)
    {
        if (!Fits(value)) return false;
        if (value.Length == 0) return directory;
        int start = 0, componentBytes = 0, components = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                char c = value[i];
                if (c < ' ' || c == '\u007f' || c == '\\' || c == ':') return false;
                if (c != '/')
                {
                    if (c < 0x80) componentBytes++;
                    else if (c < 0x800) componentBytes += 2;
                    else if (c >= 0xd800 && c <= 0xdbff) { componentBytes += 4; i++; }
                    else componentBytes += 3;
                    if (componentBytes > 255) return false;
                    continue;
                }
            }
            int length = i - start;
            if (length == 0) return directory && i == value.Length && i != 0;
            if (length == 1 && value[start] == '.' || length == 2 && value[start] == '.' && value[start + 1] == '.') return false;
            if (++components > 64) return false;
            start = i + 1;
            componentBytes = 0;
        }
        return true;
    }

    internal static int Device(string prefix)
    {
        if (Equal(prefix, "carda") || Equal(prefix, "sda")) return (int)StorageDevice.SdSlotA;
        if (Equal(prefix, "cardb") || Equal(prefix, "sdb")) return (int)StorageDevice.SdSlotB;
        if (Equal(prefix, "sd") || Equal(prefix, "sdc") || Equal(prefix, "sd2") || Equal(prefix, "sd2sp2")) return (int)StorageDevice.SdSerialPort2;
        if (Equal(prefix, "gcloader") || Equal(prefix, "gcldr")) return (int)StorageDevice.GcLoader;
        if (Equal(prefix, "dvd")) return (int)StorageDevice.DvdDrive;
        return -1;
    }

    private static bool Equal(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    internal static bool Image(string path) => path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gcm", StringComparison.OrdinalIgnoreCase);
    internal static bool UnsupportedImage(string path)
        => path.EndsWith(".nkit.iso", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".nkit.gcm", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".rvz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gcz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".wia", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wbfs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ciso", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".nkit", StringComparison.OrdinalIgnoreCase);
}

internal sealed class LaunchContentResolver
{
    private readonly string launchPath;
    private readonly LaunchContentLocation[] fallbacks;
    private readonly Func<LaunchContent, bool>? validate;
    private readonly LaunchContentImageMode imageMode;
    private readonly LaunchContentDvdSource dvdSource;
    private readonly LaunchContentAttempt[] attempts = new LaunchContentAttempt[18];
    private int attemptCount;
    private LaunchContent? content;

    internal LaunchContentResolver(string path, LaunchContentOptions options)
    {
        launchPath = path; validate = options.Validate; imageMode = options.ImageMode; dvdSource = options.DvdSource;
        if ((int)imageMode < 0 || (int)imageMode > 1 || (int)dvdSource < 0 || (int)dvdSource > 2)
            throw new ArgumentOutOfRangeException("options");
        var locations = options.Fallbacks;
        if (locations == null || locations.Length > 16) throw new ArgumentException("Supply at most 16 fallback locations.", "options");
        fallbacks = new LaunchContentLocation[locations.Length];
        for (int i = 0; i < locations.Length; i++)
        {
            var location = locations[i];
            if (location == null) throw new ArgumentException("Fallback locations cannot be null.", "options");
            ValidateConfiguration(location.Configuration);
            fallbacks[i] = location;
        }
    }

    private static void ValidateConfiguration(MountConfiguration configuration)
    {
        if (configuration.Kind != MountKind.DiscImage) return;
        string path = configuration.ImagePath;
        int colon = path.IndexOf(":/", StringComparison.Ordinal);
        if (colon <= 0 || !LaunchContentPaths.Fits(path)) throw new ArgumentException("Invalid fallback image path.");
        int device = LaunchContentPaths.Device(path.Substring(0, colon));
        if (device < 0 || device == (int)StorageDevice.DvdDrive ||
            !path.StartsWith(StorageApi.Root((StorageDevice)device), StringComparison.Ordinal) ||
            !LaunchContentPaths.ValidRelative(path.Substring(colon + 2), false) ||
            !LaunchContentPaths.Image(path) || LaunchContentPaths.UnsupportedImage(path))
            throw new ArgumentException("Use a canonical block-volume path to a plain ISO/GCM fallback image.");
        var source = configuration.SourceConfiguration;
        if (source != null && source.Device != (StorageDevice)device)
            throw new ArgumentException("The fallback image and source configuration must use the same device.");
    }

    internal LaunchContentResult Run()
    {
        if (launchPath.Length == 0) return Fallback(LaunchContentFailure.MissingLaunchPath);
        int colon = launchPath.IndexOf(":/", StringComparison.Ordinal);
        if (colon < 0)
        {
            if (LaunchContentPaths.ValidRelative(launchPath, false)) return Fallback(LaunchContentFailure.MissingLaunchPath);
            return Finish(LaunchContentFailure.InvalidPath);
        }
        if (colon == 0 || !LaunchContentPaths.Fits(launchPath)) return Finish(LaunchContentFailure.InvalidPath);
        for (int i = 0; i < colon; i++)
        {
            char c = launchPath[i];
            if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-'))
                return Finish(LaunchContentFailure.InvalidPath);
        }
        string relative = launchPath.Substring(colon + 2);
        if (!LaunchContentPaths.ValidRelative(relative, false)) return Finish(LaunchContentFailure.InvalidPath);
        int device = LaunchContentPaths.Device(launchPath.Substring(0, colon));
        if (device < 0) return Fallback(LaunchContentFailure.UnsupportedDevice);
        if (imageMode == LaunchContentImageMode.MountImage && LaunchContentPaths.UnsupportedImage(relative))
            return Finish(LaunchContentFailure.UnsupportedImage);
        bool image = imageMode == LaunchContentImageMode.MountImage && LaunchContentPaths.Image(relative);
        if (device != (int)StorageDevice.DvdDrive) return Finish(Hint((StorageDevice)device, relative, image));
        if (dvdSource != LaunchContentDvdSource.Automatic)
            return Finish(Hint(dvdSource == LaunchContentDvdSource.Disc ? StorageDevice.DvdDrive : StorageDevice.GcLoader, relative, image));
        if (validate == null) return Finish(LaunchContentFailure.AmbiguousSource);

        // Prefer a caller's established namespace. Raw GC Loader startup can
        // reset the drive, so it must never probe through a borrowed disc.
        var disc = StorageApi.GetMountInfo(StorageDevice.DvdDrive);
        if (disc.IsMounted) return Finish(Hint(StorageDevice.DvdDrive, relative, image));
        if (StorageApi.IsMounted(StorageDevice.GcLoader)) return Finish(Hint(StorageDevice.GcLoader, relative, image));
        var failure = Hint(StorageDevice.DvdDrive, relative, image);
        if (failure == LaunchContentFailure.None || failure == LaunchContentFailure.CleanupFailed || failure == LaunchContentFailure.MountChanged)
            return Finish(failure);
        return Finish(Hint(StorageDevice.GcLoader, relative, image));
    }

    private LaunchContentResult Fallback(LaunchContentFailure missing)
    {
        var failure = missing;
        for (int i = 0; i < fallbacks.Length; i++)
        {
            var location = fallbacks[i];
            failure = Candidate(location, "", false, true);
            if (failure == LaunchContentFailure.None || failure == LaunchContentFailure.CleanupFailed || failure == LaunchContentFailure.MountChanged) break;
        }
        return Finish(failure);
    }

    private static MountConfiguration ExistingOrDefault(StorageDevice device)
    {
        var info = StorageApi.GetMountInfo(device);
        if (info.IsMounted && info.LastSuccessfulConfiguration != null) return info.LastSuccessfulConfiguration;
        return device == StorageDevice.DvdDrive ? MountConfiguration.Disc() : MountConfiguration.BlockVolume(device);
    }

    private LaunchContentFailure Hint(StorageDevice device, string relative, bool image)
    {
        string sourcePath = StorageApi.Root(device) + relative;
        if (!LaunchContentPaths.Fits(sourcePath)) return LaunchContentFailure.InvalidPath;
        var configuration = ExistingOrDefault(device);
        string directory = relative.Substring(0, relative.LastIndexOf('/') + 1);
        bool checkFile = true;
        if (image)
        {
            directory = "";
            if (device == StorageDevice.DvdDrive)
            {
                checkFile = false; // An ODE's image filename is outside its FST.
                if (configuration.DiscFormat != DiscFormat.Fst) configuration = MountConfiguration.Disc();
            }
            else configuration = MountConfiguration.DiscImage(sourcePath, configuration);
        }
        return Candidate(new LaunchContentLocation(configuration, directory), sourcePath, checkFile, false);
    }

    private static bool RawMountWouldResetBorrowedDisc(StorageDevice device)
    {
        if (device != StorageDevice.GcLoader || StorageApi.IsMounted(device)) return false;
        var disc = StorageApi.GetMountInfo(StorageDevice.DvdDrive);
        return disc.IsMounted && disc.LastSuccessfulConfiguration != null && disc.LastSuccessfulConfiguration.Kind == MountKind.Disc;
    }

    private LaunchContentFailure Candidate(LaunchContentLocation location, string sourcePath, bool checkFile, bool fallback)
    {
        if (sourcePath.Length == 0 && location.Configuration.Kind == MountKind.DiscImage)
            sourcePath = location.Configuration.ImagePath;
        var owned = new LaunchContentMounts();
        LaunchContent? candidate = null;
        LaunchContentFailure failure;
        MountFailure nativeFailure = MountFailure.None;
        try
        {
            failure = Prepare(location, sourcePath, checkFile, fallback, owned, out candidate, out nativeFailure);
            if (failure == LaunchContentFailure.None && validate != null && !validate(candidate!)) failure = LaunchContentFailure.ValidationRejected;
            if (failure == LaunchContentFailure.None && !candidate!.IsValid) failure = LaunchContentFailure.MountChanged;
        }
        catch
        {
            if (candidate != null) candidate.Dispose(); else owned.Release();
            throw;
        }
        if (failure == LaunchContentFailure.None) content = candidate;
        else
        {
            try { if (candidate != null) candidate.Dispose(); else owned.Release(); }
            catch (IOException) { failure = LaunchContentFailure.CleanupFailed; }
        }
        attempts[attemptCount++] = new LaunchContentAttempt(sourcePath,
            StorageApi.Root(location.Configuration.Device) + location.Directory,
            location.Configuration.Device, failure, nativeFailure);
        return failure;
    }

    private LaunchContentFailure Prepare(LaunchContentLocation location, string sourcePath, bool checkFile,
        bool fallback, LaunchContentMounts owned, out LaunchContent? candidate, out MountFailure nativeFailure)
    {
        candidate = null; nativeFailure = MountFailure.None;
        var configuration = location.Configuration;
        MountedFileSystem? source = null;
        if (configuration.Kind == MountKind.DiscImage)
        {
            string imagePath = configuration.ImagePath;
            int colon = imagePath.IndexOf(":/", StringComparison.Ordinal);
            var device = (StorageDevice)LaunchContentPaths.Device(imagePath.Substring(0, colon));
            if (RawMountWouldResetBorrowedDisc(device))
            { nativeFailure = MountFailure.ConfigurationConflict; return LaunchContentFailure.MountFailed; }
            var sourceResult = owned.Acquire(configuration.SourceConfiguration ?? ExistingOrDefault(device));
            if (!sourceResult.Succeeded)
            { nativeFailure = sourceResult.Failure; return LaunchContentFailure.MountFailed; }
            source = sourceResult.FileSystem!;
            if (!File.Exists(imagePath)) return LaunchContentFailure.ContentNotFound;
            // Acquire the dependency explicitly, so rejection after a successful
            // image mount can release it, and borrowed dependencies stay mounted.
            configuration = MountConfiguration.DiscImage(imagePath);
            if (sourcePath.Length == 0) sourcePath = imagePath;
        }
        else if (RawMountWouldResetBorrowedDisc(configuration.Device))
        { nativeFailure = MountFailure.ConfigurationConflict; return LaunchContentFailure.MountFailed; }

        var result = owned.Acquire(configuration);
        if (!result.Succeeded)
        { nativeFailure = result.Failure; return LaunchContentFailure.MountFailed; }
        var fileSystem = result.FileSystem!;
        string root = fileSystem.RootPath + location.Directory;
        if (!Directory.Exists(root) || checkFile && !File.Exists(sourcePath)) return LaunchContentFailure.ContentNotFound;
        candidate = new LaunchContent(fileSystem, source, location.Directory, launchPath, sourcePath, fallback, owned);
        return LaunchContentFailure.None;
    }

    private LaunchContentResult Finish(LaunchContentFailure failure)
    {
        var history = new LaunchContentAttempt[attemptCount];
        Array.Copy(attempts, history, attemptCount);
        return new LaunchContentResult(content, failure, history);
    }
}
