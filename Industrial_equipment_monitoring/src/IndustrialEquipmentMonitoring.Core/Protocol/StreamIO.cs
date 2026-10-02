namespace IndustrialEquipmentMonitoring.Core.Protocol;

internal static class StreamIO
{
    public static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The remote device closed the connection.");
            }

            offset += read;
        }
    }
}
