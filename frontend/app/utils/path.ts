export function getLeafDirectoryName(fullPath: string): string {
    // Normalize the path by removing a trailing slash/backslash.
    let normalizedPath = fullPath.replace(/[/\\]$/, '');

    // Find the index of the last separator.
    const lastSlash = normalizedPath.lastIndexOf('/');
    const lastBackslash = normalizedPath.lastIndexOf('\\');
    const lastSeparatorIndex = Math.max(lastSlash, lastBackslash);

    // Extract the final component.
    // Start the substring *after* the last separator.
    const leafName = normalizedPath.substring(lastSeparatorIndex + 1);

    // If the result is empty, it means the path was a root (e.g., '/', 'C:').
    if (leafName.length === 0) {
        // Return the root component itself (e.g., '/')
        return normalizedPath;
    }

    return leafName;
}