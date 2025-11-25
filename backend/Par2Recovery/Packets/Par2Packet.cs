using System.Runtime.InteropServices;

namespace NzbWebDAV.Par2Recovery.Packets
{
    /// <summary>
    /// Implements the basic Read mechanism, passing the body bytes to any child class.
    /// </summary>
    public class Par2Packet
    {
        public Par2PacketHeader Header { get; protected set; }

        public Par2Packet(Par2PacketHeader header)
        {
            Header = header;
        }

        public async Task ReadAsync(Stream stream)
        {
            // Determine the length of the body as the given packet length, minus the length of the header.
            var bodyLength = Header.PacketLength - (ulong)Marshal.SizeOf<Par2PacketHeader>();

            // Read the calculated number of bytes from the stream.
            var body = new byte[bodyLength];
            await stream.ReadExactlyAsync(body.AsMemory(0, (int)bodyLength)).ConfigureAwait(false);

            // Pass the body to the further implementation for parsing.
            ParseBody(body);
        }

        protected virtual void ParseBody(byte[] body)
        {
            // intentionally left blank
        }
    }
}