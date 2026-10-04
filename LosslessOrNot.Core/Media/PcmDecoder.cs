using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Core.Media;

/// <summary>Decodes audio to mono float PCM by piping it out of ffmpeg.</summary>
internal static class PcmDecoder
{
    /// <summary>
    /// Streams mono float32 PCM at <paramref name="sampleRate"/> (the file's native
    /// rate), starting at <paramref name="startSec"/> and lasting at most
    /// <paramref name="durationSec"/>.
    /// </summary>
    /// <exception cref="AudioAnalysisException">ffmpeg produced less than one FFT frame of audio.</exception>
    public static async Task<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath,
        string ffmpegExe,
        int sampleRate,
        double startSec,
        double durationSec,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int capacity = (int)(durationSec * sampleRate) + SpectralAnalyzer.FftSize;
        var samples = new float[capacity];
        int count = 0;

        using Process process = ProcessRunner.Start(ffmpegExe, BuildArguments(filePath, sampleRate, startSec, durationSec));
        // Drained concurrently so ffmpeg can never block on a full stderr pipe.
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using (cancellationToken.Register(static p => ProcessRunner.Kill((Process)p!), process))
        {
            Stream stdout = process.StandardOutput.BaseStream;
            var buffer = new byte[4 * 8192];
            bool endOfStream = false;
            try
            {
                while (count < capacity)
                {
                    int n = await stdout
                        .ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, CancellationToken.None)
                        .ConfigureAwait(false);
                    count += CopySamples(buffer, n, samples, count);
                    if (n < buffer.Length)
                    {
                        endOfStream = true;
                        break;
                    }
                }
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                // The pipe broke because the process was killed on cancellation.
            }
            if (!endOfStream)
            {
                // Everything needed has been read: stop the decoder rather than leave
                // it blocked writing into a pipe nobody reads any more.
                ProcessRunner.Kill(process);
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        string errors = await stderr.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (count < SpectralAnalyzer.FftSize)
        {
            string why = ProcessRunner.FirstLine(errors);
            throw new AudioAnalysisException(why.Length > 0
                ? $"Pas assez d'audio décodé ({count} échantillons) — ffmpeg : {why}"
                : $"Pas assez d'audio décodé ({count} échantillons).");
        }
        return samples.AsMemory(0, count);
    }

    // Every number is formatted with the invariant culture: on a French system the
    // current culture would hand ffmpeg "45,00", which it rejects.
    internal static string[] BuildArguments(string filePath, int sampleRate, double startSec, double durationSec) =>
    [
        "-v", "error",
        "-nostdin",
        "-ss", startSec.ToString("0.00", CultureInfo.InvariantCulture),
        "-i", filePath,
        "-t", durationSec.ToString("0.00", CultureInfo.InvariantCulture),
        // The first audio stream only: embedded cover art is a video stream.
        "-map", "0:a:0",
        "-ac", "1",
        "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
        "-f", "f32le",
        "-",
    ];

    // Converts whole little-endian float32 samples. A trailing partial sample, which
    // can only occur at the end of the stream, is dropped as Go's io.ReadFull loop did.
    private static int CopySamples(byte[] buffer, int byteCount, float[] samples, int offset)
    {
        int n = Math.Min(byteCount / sizeof(float), samples.Length - offset);
        ReadOnlySpan<byte> source = buffer.AsSpan(0, n * sizeof(float));
        Span<float> destination = samples.AsSpan(offset, n);
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, float>(source).CopyTo(destination);
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                destination[i] = BinaryPrimitives.ReadSingleLittleEndian(source[(i * sizeof(float))..]);
            }
        }
        return n;
    }
}
