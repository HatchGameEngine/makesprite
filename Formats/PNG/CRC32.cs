// Code portions taken from the public domain library BigGustave
namespace PNG {
    public static class CRC32 {
        private const uint Polynomial = 0xEDB88320;

        private static readonly uint[] Lookup;

        static CRC32() {
            Lookup = new uint[256];
            for (uint i = 0; i < 256; i++) {
                uint value = i;
                for (int j = 0; j < 8; ++j) {
                    if ((value & 1) != 0) {
                        value = (value >> 1) ^ Polynomial;
                    }
                    else {
                        value >>= 1;
                    }
                }

                Lookup[i] = value;
            }
        }

        public static uint Calculate(byte[] data) {
            uint crc32 = uint.MaxValue;
            for (int i = 0; i < data.Length; i++) {
                uint index = (crc32 ^ data[i]) & 0xFF;
                crc32 = (crc32 >> 8) ^ Lookup[index];
            }

            return crc32 ^ uint.MaxValue;
        }

        public static uint Calculate(List<byte> data) {
            uint crc32 = uint.MaxValue;
            for (int i = 0; i < data.Count; i++) {
                uint index = (crc32 ^ data[i]) & 0xFF;
                crc32 = (crc32 >> 8) ^ Lookup[index];
            }

            return crc32 ^ uint.MaxValue;
        }

        public static uint Calculate(byte[] data, byte[] data2) {
            uint crc32 = uint.MaxValue;
            for (int i = 0; i < data.Length; i++) {
                uint index = (crc32 ^ data[i]) & 0xFF;
                crc32 = (crc32 >> 8) ^ Lookup[index];
            }

            for (int i = 0; i < data2.Length; i++) {
                uint index = (crc32 ^ data2[i]) & 0xFF;
                crc32 = (crc32 >> 8) ^ Lookup[index];
            }

            return crc32 ^ uint.MaxValue;
        }
    }
}
