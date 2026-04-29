using System;

// Code portions taken from the public domain library BigGustave
namespace PNG {
    public static class Decoder {
        public static byte[] Decode(byte[] decompressedData, File file) {
            int bytesPerPixel = file.BytesPerPixel;
            int samplesPerPixel = file.GetSamplesPerPixel();

            switch (file.InterlaceMethod) {
            case InterlaceMethod.None: {
                int bytesPerScanline = file.GetBytesPerScanline();
                int currentRowStartByteAbsolute = 1;

                for (int rowIndex = 0; rowIndex < file.Height; rowIndex++) {
                    FilterType filterType = (FilterType)decompressedData[currentRowStartByteAbsolute - 1];
                    int previousRowStartByteAbsolute = rowIndex + (bytesPerScanline * (rowIndex - 1));
                    int end = currentRowStartByteAbsolute + bytesPerScanline;
                    for (int currentByteAbsolute = currentRowStartByteAbsolute; currentByteAbsolute < end; currentByteAbsolute++) {
                        ReverseFilter(decompressedData, filterType, previousRowStartByteAbsolute, currentRowStartByteAbsolute, currentByteAbsolute, currentByteAbsolute - currentRowStartByteAbsolute, bytesPerPixel);
                    }

                    currentRowStartByteAbsolute += bytesPerScanline + 1;
                }

                return decompressedData;
            }
            case InterlaceMethod.Adam7: {
                int pixelsPerRow = file.Width * bytesPerPixel;
                byte[] newBytes = new byte[file.Height * pixelsPerRow];
                int i = 0;
                int previousStartRowByteAbsolute = -1;

                // 7 passes
                for (int pass = 0; pass < 7; pass++) {
                    int numberOfScanlines = Adam7.GetNumberOfScanlinesInPass(file.Height, pass);
                    int numberOfPixelsPerScanline = Adam7.GetPixelsPerScanlineInPass(file.Width, pass);
                    if (numberOfScanlines <= 0 || numberOfPixelsPerScanline <= 0) {
                        continue;
                    }

                    for (int scanlineIndex = 0; scanlineIndex < numberOfScanlines; scanlineIndex++) {
                        FilterType filterType = (FilterType)decompressedData[i++];
                        int rowStartByte = i;

                        for (int j = 0; j < numberOfPixelsPerScanline; j++) {
                            (int x, int y) pixelIndex = Adam7.GetPixelIndexForScanlineInPass(pass, scanlineIndex, j);
                            for (int k = 0; k < bytesPerPixel; k++) {
                                int byteLineNumber = (j * bytesPerPixel) + k;
                                ReverseFilter(decompressedData, filterType, previousStartRowByteAbsolute, rowStartByte, i, byteLineNumber, bytesPerPixel);
                                i++;
                            }

                            int start = pixelsPerRow * pixelIndex.y + pixelIndex.x * bytesPerPixel;
                            Array.ConstrainedCopy(decompressedData, rowStartByte + j * bytesPerPixel, newBytes, start, bytesPerPixel);
                        }

                        previousStartRowByteAbsolute = rowStartByte;
                    }
                }

                return newBytes;
            }
            default:
                throw new ArgumentOutOfRangeException($"Invalid interlace method {file.InterlaceMethod}");
            }
        }

        private static void ReverseFilter(byte[] data, FilterType type, int previousRowStartByteAbsolute, int rowStartByteAbsolute, int byteAbsolute, int rowByteIndex, int bytesPerPixel) {
            byte GetLeftByteValue() {
                int leftIndex = rowByteIndex - bytesPerPixel;
                byte leftValue = leftIndex >= 0 ? data[rowStartByteAbsolute + leftIndex] : (byte)0;
                return leftValue;
            }

            byte GetAboveByteValue() {
                int upIndex = previousRowStartByteAbsolute + rowByteIndex;
                return upIndex >= 0 ? data[upIndex] : (byte)0;
            }

            byte GetAboveLeftByteValue() {
                int index = previousRowStartByteAbsolute + rowByteIndex - bytesPerPixel;
                return index < previousRowStartByteAbsolute || previousRowStartByteAbsolute < 0 ? (byte)0 : data[index];
            }

            if (type == FilterType.Up) {
                int above = previousRowStartByteAbsolute + rowByteIndex;
                if (above < 0) {
                    return;
                }

                data[byteAbsolute] += data[above];
                return;
            }
            else if (type == FilterType.Sub) {
                int leftIndex = rowByteIndex - bytesPerPixel;
                if (leftIndex < 0) {
                    return;
                }

                data[byteAbsolute] += data[rowStartByteAbsolute + leftIndex];
                return;
            }

            switch (type) {
            case FilterType.None:
                return;
            case FilterType.Average:
                data[byteAbsolute] += (byte)((GetLeftByteValue() + GetAboveByteValue()) / 2);
                break;
            case FilterType.Paeth:
                byte a = GetLeftByteValue();
                byte b = GetAboveByteValue();
                byte c = GetAboveLeftByteValue();
                data[byteAbsolute] += GetPaethValue(a, b, c);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static byte GetPaethValue(byte a, byte b, byte c) {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc) {
                return a;
            }

            return pb <= pc ? b : c;
        }
    }
}
