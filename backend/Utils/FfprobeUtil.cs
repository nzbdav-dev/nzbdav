using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFMpegCore;
using Serilog;

namespace NzbWebDAV.Utils;

public static class FfprobeUtil
{
    /// <summary>
    /// Checks if a stream contains valid video/audio content using FFMpegCore
    /// </summary>
    /// <param name="stream">The stream to analyze</param>
    /// <param name="filePath">Optional file path for logging purposes</param>
    /// <param name="enableMp4DeepScan">Whether to use the slower but more thorough MP4 stdin workaround</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the stream contains valid media content, false if corrupt or invalid</returns>
    public static async Task<bool> IsValidMediaStreamAsync(Stream stream, string? filePath = null, bool enableMp4DeepScan = false, CancellationToken ct = default)
    {
        // Check if this is an MP4 file that might need the stdin workaround
        var isMp4File = !string.IsNullOrEmpty(filePath) && IsMp4File(filePath);

        try
        {
            Log.Debug("Analyzing media stream using FFMpegCore for {FilePath}", filePath ?? "unknown file");

            // Use FFMpegCore to analyze the stream
            var mediaInfo = await FFProbe.AnalyseAsync(stream, cancellationToken: ct);

            // Check if we have any video or audio streams
            var hasVideo = mediaInfo.VideoStreams.Any();
            var hasAudio = mediaInfo.AudioStreams.Any();
            var hasValidContent = hasVideo && hasAudio;

            if (hasValidContent)
            {
                var streamSummary = CreateStreamSummary(mediaInfo);
                Log.Information("Media analysis PASSED for {FilePath}: {StreamSummary}",
                    filePath ?? "stream", streamSummary);
                return true;
            }
            else
            {
                Log.Warning("Media analysis FAILED for {FilePath}: No video or audio streams found",
                    filePath ?? "stream");
                return false;
            }
        }
        catch (Exception ex) when (isMp4File && IsMoovAtomError(ex))
        {
            if (enableMp4DeepScan)
            {
                Log.Debug("MP4 file detected, using ffprobe stdin workaround for {FilePath}", filePath);
                return await AnalyzeMp4WithStdinAsync(stream, filePath, ct);
            }

            Log.Information("MP4 file {FilePath} has moov atom issues but MP4 deep scan is disabled - considering valid. " +
                           "Enable 'MP4 Deep Scan' in settings for thorough validation. Error: {Error}",
                           filePath, ex.Message);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error analyzing media stream for {FilePath}", filePath ?? "stream");
            return false;
        }
    }

    /// <summary>
    /// Creates a human-readable summary of media streams
    /// </summary>
    private static string CreateStreamSummary(IMediaAnalysis mediaInfo)
    {
        var summary = new List<string>();

        if (mediaInfo.VideoStreams.Any())
        {
            var videoCodecs = mediaInfo.VideoStreams.Select(v => v.CodecName).Distinct();
            summary.Add($"{mediaInfo.VideoStreams.Count()} video ({string.Join(", ", videoCodecs)})");
        }

        if (mediaInfo.AudioStreams.Any())
        {
            var audioCodecs = mediaInfo.AudioStreams.Select(a => a.CodecName).Distinct();
            summary.Add($"{mediaInfo.AudioStreams.Count()} audio ({string.Join(", ", audioCodecs)})");
        }

        if (mediaInfo.SubtitleStreams.Any())
        {
            summary.Add($"{mediaInfo.SubtitleStreams.Count()} subtitle");
        }

        if (mediaInfo.Duration != TimeSpan.Zero)
        {
            summary.Add($"{mediaInfo.Duration.TotalSeconds:F1}s");
        }

        return summary.Count > 0 ? string.Join(", ", summary) : "Unknown format";
    }

    /// <summary>
    /// Checks if a file is an MP4 container format
    /// </summary>
    private static bool IsMp4File(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".mp4" or ".m4v" or ".mov" or ".3gp" or ".3g2";
    }

    /// <summary>
    /// Checks if an exception is related to moov atom issues in MP4 files
    /// </summary>
    private static bool IsMoovAtomError(Exception ex)
    {
        return ex.Message?.ToLowerInvariant().Contains("moov atom not found") ?? false;
    }

    /// <summary>
    /// Analyzes MP4 files using ffprobe with stdin to work around moov atom issues
    /// </summary>
    private static async Task<bool> AnalyzeMp4WithStdinAsync(Stream stream, string? filePath, CancellationToken ct)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("ffprobe",
                    "-loglevel error -print_format json -show_format -show_streams -")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                }
            };

            if (!process.Start())
            {
                Log.Error("Failed to start ffprobe process for MP4 analysis");
                return false;
            }

            // Copy the stream data to ffprobe's stdin and close it when done
            try
            {
                await stream.CopyToAsync(process.StandardInput.BaseStream, ct);
            }
            catch (ObjectDisposedException)
            {
                // ffprobe closed stdin early - this is normal behavior
                Log.Debug("ffprobe closed stdin early for MP4 file {FilePath} - this is normal", filePath);
            }
            catch (IOException ex) when (ex.Message.Contains("Broken pipe") || ex.Message.Contains("Pipe is broken"))
            {
                // ffprobe closed stdin early - this is normal behavior
                Log.Debug("ffprobe closed stdin pipe early for MP4 file {FilePath} - this is normal", filePath);
            }

            // Try to close stdin gracefully
            try
            {
                process.StandardInput.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed - ignore
            }
            catch (IOException)
            {
                // Pipe already closed - ignore
            }

            // Wait for the process to complete
            await process.WaitForExitAsync(ct);

            // Read the output
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);

            if (process.ExitCode != 0)
            {
                Log.Debug("ffprobe failed for MP4 file {FilePath}: Exit code {ExitCode}, Error: {Error}",
                    filePath, process.ExitCode, error);
                return false;
            }

            // Parse the JSON output
            if (string.IsNullOrWhiteSpace(output))
            {
                Log.Debug("ffprobe returned empty output for MP4 file {FilePath}", filePath);
                return false;
            }

            var mediaInfo = JsonSerializer.Deserialize<FfprobeOutput>(output);
            if (mediaInfo?.Streams == null)
            {
                Log.Debug("Failed to parse ffprobe output for MP4 file {FilePath}", filePath);
                return false;
            }

            // Check if we have any video or audio streams
            var hasVideo = mediaInfo.Streams.Any(s => s.CodecType == "video");
            var hasAudio = mediaInfo.Streams.Any(s => s.CodecType == "audio");
            var hasValidContent = hasVideo && hasAudio;

            if (hasValidContent)
            {
                var streamSummary = CreateMp4StreamSummary(mediaInfo);
                Log.Information("MP4 analysis PASSED for {FilePath}: {StreamSummary}",
                    filePath ?? "stream", streamSummary);
                return true;
            }
            else
            {
                Log.Warning("MP4 analysis FAILED for {FilePath}: No video or audio streams found",
                    filePath ?? "stream");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error analyzing MP4 stream for {FilePath}", filePath ?? "stream");
            return false;
        }
    }

    /// <summary>
    /// Creates a human-readable summary from ffprobe JSON output
    /// </summary>
    private static string CreateMp4StreamSummary(FfprobeOutput mediaInfo)
    {
        var summary = new List<string>();

        var videoStreams = mediaInfo.Streams?.Where(s => s.CodecType == "video").ToList() ?? new List<FfprobeStream>();
        var audioStreams = mediaInfo.Streams?.Where(s => s.CodecType == "audio").ToList() ?? new List<FfprobeStream>();
        var subtitleStreams = mediaInfo.Streams?.Where(s => s.CodecType == "subtitle").ToList() ?? new List<FfprobeStream>();

        if (videoStreams.Any())
        {
            var videoCodecs = videoStreams.Select(v => v.CodecName).Where(c => !string.IsNullOrEmpty(c)).Distinct();
            summary.Add($"{videoStreams.Count} video ({string.Join(", ", videoCodecs)})");
        }

        if (audioStreams.Any())
        {
            var audioCodecs = audioStreams.Select(a => a.CodecName).Where(c => !string.IsNullOrEmpty(c)).Distinct();
            summary.Add($"{audioStreams.Count} audio ({string.Join(", ", audioCodecs)})");
        }

        if (subtitleStreams.Any())
        {
            summary.Add($"{subtitleStreams.Count} subtitle");
        }

        if (mediaInfo.Format?.Duration != null && double.TryParse(mediaInfo.Format.Duration, out var duration))
        {
            summary.Add($"{duration:F1}s");
        }

        return summary.Count > 0 ? string.Join(", ", summary) : "Unknown format";
    }

    /// <summary>
    /// Simple classes to deserialize ffprobe JSON output
    /// </summary>
    private class FfprobeOutput
    {
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }

        [JsonPropertyName("format")]
        public FfprobeFormat? Format { get; set; }
    }

    private class FfprobeStream
    {
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }
    }

    private class FfprobeFormat
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; set; }
    }
}