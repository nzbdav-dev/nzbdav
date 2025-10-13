export function formatFileSize(bytes: number | null | undefined) {
    var suffix = "B";
    if (bytes === null || bytes === undefined) return "unknown size"
    if (bytes >= 1024) { bytes /= 1024; suffix = "KB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "MB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "GB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "TB"; }
    if (bytes >= 1024) { bytes /= 1024; suffix = "PB"; }
    return `${parseFloat(bytes.toFixed(2))} ${suffix}`;
}