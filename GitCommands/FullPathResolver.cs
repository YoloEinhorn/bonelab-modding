using GitUIPluginInterfaces;

namespace GitCommands
{
    public sealed class FullPathResolver : IFullPathResolver
    {
        private readonly Func<string> _getWorkingDir;

        public FullPathResolver(Func<string> getWorkingDir)
        {
            _getWorkingDir = getWorkingDir;
        }

        /// <inheritdoc />
        /// <summary>
        /// Resolves the provided path (folder or file) against the current working directory.
        /// </summary>
        /// <param name="path">Folder or file path to resolve.</param>
        /// <returns>
        /// <paramref name="path" /> if <paramref name="path" /> is rooted; otherwise resolved path from working directory of the current repository.
        /// </returns>
        /// <exception cref="PathTooLongException">The resolved path is too long (greater than 248 characters).</exception>
        public string? Resolve(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string workingDir = _getWorkingDir();
            if (string.IsNullOrWhiteSpace(workingDir))
            {
                workingDir = Environment.CurrentDirectory;
            }

            string basePath = Path.GetFullPath(workingDir);
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
                && !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }

            return PathUtil.Resolve(basePath, path);
        }
    }
}
