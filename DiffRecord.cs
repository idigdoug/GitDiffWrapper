// Copyright (c) Doug Cook. All rights reserved.
// Licensed under the MIT License.

using System;
using CultureInfo = System.Globalization.CultureInfo;
using StringBuilder = System.Text.StringBuilder;

namespace GitDiffWrapper
{
    internal sealed class DiffRecord
    {
        public readonly int SrcMode;
        public readonly int DstMode;
        public readonly string SrcHash;
        public readonly string DstHash;
        public readonly char Status;
        public readonly int Score;
        public readonly string SrcPath;
        public readonly string DstPath;

        public DiffRecord(string src)
        {
            Logger.Verbose(src);

            int i = 0;
            i = SkipChar(src, i, ':');
            i = ReadOctal(src, i, out this.SrcMode);
            i = SkipChar(src, i, ' ');
            i = ReadOctal(src, i, out this.DstMode);
            i = SkipChar(src, i, ' ');
            i = ReadHash(src, i, out this.SrcHash);
            i = SkipChar(src, i, ' ');
            i = ReadHash(src, i, out this.DstHash);
            i = SkipChar(src, i, ' ');
            i = ReadChar(src, i, out this.Status);
            i = ReadDecimal(src, i, out this.Score, true);
            i = SkipChar(src, i, '\t');
            i = ReadString(src, i, out this.SrcPath);
            this.SrcPath = this.SrcPath.Replace('/', '\\');

            if (i < src.Length)
            {
                i = SkipChar(src, i, '\t');
                i = ReadString(src, i, out this.DstPath);
                this.DstPath = this.DstPath.Replace('/', '\\');
            }
            else
            {
                this.DstPath = this.SrcPath;
            }
        }

        public DiffRecord(string name, Side side)
        {
            bool r = side == Side.Right;
            Logger.Verbose("Untracked: {0}", name);
            this.SrcMode = r ? 0 : 0x81A4;
            this.DstMode = r ? 0x81A4 : 0;
            this.SrcHash = "0";
            this.DstHash = "0";
            this.Status = r ? 'A' : 'D';
            this.Score = 0;
            this.SrcPath = name;
            this.DstPath = name;
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "0x{0:x} 0x{1:x} {2} {3} {4}{5} \"{6}\" \"{7}\"",
                this.SrcMode.ToString(CultureInfo.InvariantCulture),
                this.DstMode.ToString(CultureInfo.InvariantCulture),
                this.SrcHash,
                this.DstHash,
                this.Status.ToString(),
                this.Score.ToString(CultureInfo.InvariantCulture),
                this.SrcPath,
                this.DstPath);
        }

        private static int SkipChar(string src, int i, char ch)
        {
            if (src.Length <= i || src[i] != ch)
            {
                throw new FormatException(
                    "Expected '" + ch + "' at position " + i.ToString(CultureInfo.InvariantCulture));
            }

            return i + 1;
        }

        private static int ReadChar(string src, int i, out char ch)
        {
            if (src.Length <= i)
            {
                throw new FormatException(
                    "Expected char at position " + i.ToString(CultureInfo.InvariantCulture));
            }

            ch = src[i];
            return i + 1;
        }

        private static int ReadOctal(string src, int i, out int n)
        {
            n = 0;

            int j = i;
            for (; j < src.Length; j += 1)
            {
                char ch = src[j];
                if (ch < '0' || '7' < ch)
                {
                    break;
                }

                n = checked(n * 8 + (src[j] - '0'));
            }

            if (i == j)
            {
                throw new FormatException(
                    "Expected octal at position " + i.ToString(CultureInfo.InvariantCulture));
            }

            return j;
        }

        private static int ReadDecimal(string src, int i, out int n, bool optional = false)
        {
            n = 0;

            int j = i;
            for (; j < src.Length; j += 1)
            {
                char ch = src[j];
                if (ch < '0' || '9' < ch)
                {
                    break;
                }

                n = checked(n * 10 + (src[j] - '0'));
            }

            if (i == j && !optional)
            {
                throw new FormatException(
                    "Expected decimal at position " + i.ToString(CultureInfo.InvariantCulture));
            }

            return j;
        }

        private static int ReadHash(string src, int i, out string hash)
        {
            bool nonzero = false;
            int j = i;
            for (; j < src.Length; j += 1)
            {
                char ch = src[j];
                if ((uint)(ch - '0') >= 10 &&
                    (uint)(ch - 'a') >= 6 &&
                    (uint)(ch - 'A') >= 6)
                {
                    break;
                }

                nonzero |= ch != '0';
            }

            if (i == j)
            {
                throw new FormatException("Expected hex at position " + i.ToString());
            }

            hash = nonzero ? src.Substring(i, j - i) : "0";
            for (; j < src.Length; j += 1)
            {
                char ch = src[j];
                if (ch != '.')
                {
                    break;
                }
            }

            return j;
        }

        private static int ReadString(string src, int i, out string str)
        {
            int j = i;
            StringBuilder sb = null;
            for (; j < src.Length; j += 1)
            {
                char ch = src[j];
                if (ch == '\t')
                {
                    break;
                }

                if (ch == '"')
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(src, i, j - i, 0);
                    }

                    continue;
                }
                else if (ch == '\\')
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(src, i, j - i, 0);
                    }

                    j += 1;
                    if (j == src.Length)
                    {
                        throw new FormatException("Unexpected end of string");
                    }

                    ch = src[j];
                    switch (ch)
                    {
                        case '\\': ch = '\\'; break;
                        case 'r': ch = '\r'; break;
                        case 'n': ch = '\n'; break;
                        case 't': ch = '\t'; break;

                        case '0': case '1': case '2': case '3':
                            j = ReadUtf8(src, j, sb) - 1;
                            continue;
                    }
                }

                if (sb != null)
                {
                    sb.Append(ch);
                }
            }

            str = sb == null ? src.Substring(i, j - i) : sb.ToString();
            return j;
        }

        private static int ReadUtf8(string src, int i, StringBuilder sb)
        {
            int ch;
            int moreBytes;
            int n;
            int j = i;
            j = ReadOctal(src, j, out n);
            if (n <= 0x7f)
            {
                // 00..7f = 1 byte
                ch = (char)n;
                moreBytes = 0;
            }
            else if (n <= 0xbf)
            {
                // 80..bf = ERROR
                throw new FormatException(
                    "Invalid utf8 at position " + j.ToString(CultureInfo.InvariantCulture));
            }
            else if (n <= 0xdf)
            {
                // c0..df = 2 bytes
                ch = (char)(n & 0x1f);
                moreBytes = 1;
            }
            else if (n <= 0xef)
            {
                // e0..ef = 3 bytes
                ch = (char)(n & 0x0f);
                moreBytes = 2;
            }
            else if (n <= 0xf7)
            {
                // f0..f7 = 4 bytes
                ch = (char)(n & 0x07);
                moreBytes = 3;
            }
            else
            {
                // f8..ff = ERROR
                throw new FormatException(
                    "Invalid utf8 at position " + j.ToString(CultureInfo.InvariantCulture));
            }

            for (; moreBytes != 0; moreBytes -= 1)
            {
                j = SkipChar(src, j, '\\');
                j = ReadOctal(src, j, out n);
                if (n < 0x80 || 0xbf < n)
                {
                    throw new FormatException(
                        "Invalid utf8 at position " + j.ToString(CultureInfo.InvariantCulture));
                }

                ch = (char)(((int)ch << 6) | (n & 0x3f));
            }

            if (ch < 0x10000)
            {
                sb.Append((char)ch);
            }
            else
            {
                ch -= 0x10000;
                sb.Append((char)(0xd800 + (ch >> 10)));
                sb.Append((char)(0xdc00 + (ch & 0x3ff)));
            }

            return j;
        }
    }
}
