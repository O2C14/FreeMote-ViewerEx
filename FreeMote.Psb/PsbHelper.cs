﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeMote.Psb
{
    internal static class PsbHelper
    {
        /// <summary>
        /// Black magic to get size hehehe...
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static int GetSize(this int i)
        {
            bool neg = false;
            if (i < 0)
            {
                neg = true;
                i = Math.Abs(i);
            }
            var l = i.ToString("X").Length;
            if (l % 2 != 0)
            {
                l++;
            }
            l = l / 2;
            if (neg)
            {
                l++;
            }
            if (l > 4)
            {
                l = 4;
            }
            return l;
        }

        /// <summary>
        /// Black magic to get size hehehe...
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static int GetSize(this uint i)
        {
            var l = i.ToString("X").Length;
            if (l % 2 != 0)
            {
                l++;
            }
            l = l / 2;
            if (l > 4)
            {
                l = 4;
            }
            return l;
        }

        /// <summary>
        /// Shorten number bytes
        /// </summary>
        /// <param name="i"></param>
        /// <param name="size">Fix size</param>
        /// <returns></returns>
        public static byte[] ZipNumberBytes(this int i, int size = 0)
        {
            return BitConverter.GetBytes(i).Take(size <= 0 ? i.GetSize() : size).ToArray();
        }

        /// <summary>
        /// Shorten number bytes
        /// </summary>
        /// <param name="i"></param>
        /// <param name="size">Fix size</param>
        /// <returns></returns>
        public static byte[] ZipNumberBytes(this uint i, int size = 0)
        {
            return BitConverter.GetBytes(i).Take(size <= 0 ? i.GetSize() : size).ToArray();
        }

        public static byte[] UnzipNumberBytes(this byte[] b, int size = 8, bool unsigned = false)
        {
            byte[] r = new byte[size];
            if (!unsigned && (b.Last() >= 0b10000000)) //negative
            {
                for (int i = 0; i < size; i++)
                {
                    r[i] = 0xFF;
                }
                b.CopyTo(r, 0);
            }
            else
            {
                b.CopyTo(r, 0);
            }
            return r;
        }

        public static long UnzipNumber(this byte[] b)
        {
            return BitConverter.ToInt64(b.UnzipNumberBytes(), 0);
        }

        public static uint UnzipUInt(this byte[] b)
        {
            return BitConverter.ToUInt32(b.UnzipNumberBytes(4, true), 0);
        }

        public static string ReadStringZeroTrim(this BinaryReader br)
        {
            StringBuilder sb = new StringBuilder();
            while (br.PeekChar() != 0)
            {
                sb.Append(br.ReadChar());
            }
            return sb.ToString();
        }

        public static void WriteStringZeroTrim(this BinaryWriter bw, string str)
        {
            bw.Write(str.ToCharArray());
            bw.Write((byte)0);
        }

        public static void Pad(this BinaryWriter bw, int length, byte paddingByte = 0x0)
        {
            if (length <= 0)
            {
                return;
            }

            if (paddingByte == 0x0)
            {
                bw.Write(new byte[length]);
                return;
            }

            for (int i = 0; i < length; i++)
            {
                bw.Write(paddingByte);
            }
        }

        /// <summary>
        /// Get Corresponding PixelFormat
        /// </summary>
        /// <param name="spec"></param>
        /// <returns></returns>
        public static PsbPixelFormat PixelFormat(this PsbSpec spec)
        {
            switch (spec)
            {
                case PsbSpec.common:
                    return PsbPixelFormat.CommonRGBA8;
                case PsbSpec.krkr:
                case PsbSpec.win:
                    return PsbPixelFormat.WinRGBA8;
                case PsbSpec.other:
                default:
                    return PsbPixelFormat.None;
            }
        }
    }
}