using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NzbWebDAV.Par2Recovery.Packets;
using Serilog;

namespace NzbWebDAV.Par2Recovery
{
    public class Par2
    {
        internal static readonly Regex ParVolume = new(
            @"(.+)\.vol[0-9]{1,10}\+[0-9]{1,10}\.par2$",
            RegexOptions.IgnoreCase
        );

        private const string Par2PacketHeaderMagic = "PAR2\0PKT";

        public static async IAsyncEnumerable<FileDesc> ReadFileDescriptions
        (
            Stream stream,
            CancellationToken ct = default
        )
        {
            Par2Packet? packet = null;
            while (stream.Position < stream.Length && !ct.IsCancellationRequested)
            {
                try
                {
                    packet = await ReadPacketAsync(stream).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to read par2 packet: {e.Message}");
                    yield break;
                }

                if (packet is FileDesc newFile)
                {
                    yield return newFile;
                }
            }
        }

        private static async Task<Par2Packet> ReadPacketAsync(Stream stream)
        {
            // Read a Packet Header.
            var header = await ReadStructAsync<Par2PacketHeader>(stream).ConfigureAwait(false);

            // Test if the magic constant matches.
            var magic = Encoding.ASCII.GetString(header.Magic);
            if (!Par2PacketHeaderMagic.Equals(magic))
                throw new ApplicationException("Invalid Magic Constant");

            // Determine which type of packet we have.
            var packetType = Encoding.ASCII.GetString(header.PacketType);
            Par2Packet result;
            switch (packetType)
            {
                case FileDesc.PacketType:
                    result = new FileDesc(header);
                    break;
                default:
                    result = new Par2Packet(header);
                    break;
            }

            // Let the packet type parse more of the stream as needed.
            await result.ReadAsync(stream).ConfigureAwait(false);

            return result;
        }

        /// <summary>
        /// Read a struct as binary from a stream.
        /// </summary>
        /// <typeparam name="T">The struct to read.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The struct with values read from the stream.</returns>
        private static async Task<T> ReadStructAsync<T>(Stream stream) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var buffer = new byte[size];
            await stream.ReadExactlyAsync(buffer.AsMemory(0, size)).ConfigureAwait(false);
            var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var structure = Marshal.PtrToStructure<T>(pinnedBuffer.AddrOfPinnedObject());
                return structure;
            }
            finally
            {
                pinnedBuffer.Free();
            }
        }

        private static T ReadStruct<T>(byte[] bytes) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            if (bytes.Length < size)
            {
                throw new ArgumentException("Byte array is too short to represent the struct.", nameof(bytes));
            }

            var pinnedBuffer = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var structure = Marshal.PtrToStructure<T>(pinnedBuffer.AddrOfPinnedObject());
                return structure;
            }
            finally
            {
                pinnedBuffer.Free();
            }
        }

        public static bool HasPar2MagicBytes(byte[] bytes)
        {
            try
            {
                var header = ReadStruct<Par2PacketHeader>(bytes);
                var magic = Encoding.ASCII.GetString(header.Magic);
                return Par2PacketHeaderMagic.Equals(magic);
            }
            catch (Exception e)
            {
                return false;
            }
        }
    }
}