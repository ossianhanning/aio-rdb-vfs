namespace MediaCluster.RealDebrid.Helpers;

public static class StringExtensions
{
    public static string GetFileName(this string path) => Path.GetFileName(path);

    public static string GetDirectory(this string path) => Path.GetDirectoryName(path);

    public static string[] GetPathComponents(this string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string GetRelativePath(this string path, string basePath) => Path.GetRelativePath(basePath, path);

    public static string[] GetRelativePathParts(this string path, string basePath) => 
        Path.GetRelativePath(basePath, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string GetFileNameWithoutExtension(this string path) => Path.GetFileNameWithoutExtension(path);

    public static string GetExtension(this string path) => Path.GetExtension(path);

    public static string GetExtensionFromFileName(this string fileName) => Path.GetExtension(fileName);
    
    public static bool IsFile(this string path) => File.Exists(path);

    public static bool IsDirectory(this string path) => Directory.Exists(path);

    public static bool PathExists(this string path) => File.Exists(path) || Directory.Exists(path);

    public static PathType GetPathType(this string path)
    {
        if (File.Exists(path)) return PathType.File;
        if (Directory.Exists(path)) return PathType.Directory;
        return PathType.DoesNotExist;
    }

    public enum PathType
    {
        File,
        Directory,
        DoesNotExist
    }
}