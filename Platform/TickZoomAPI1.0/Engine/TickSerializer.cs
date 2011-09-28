using System.IO;

namespace TickZoom.Api
{
    public interface TickSerializer
    {
        void ToWriter(ref TickBinary binary, MemoryStream writer);
        int FromReader(ref TickBinary binary, MemoryStream reader);
        int FromReader(ref TickBinary binary, byte dataVersion, BinaryReader reader);
    }
}