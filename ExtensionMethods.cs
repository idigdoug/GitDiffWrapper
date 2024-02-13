// Copyright (c) Doug Cook. All rights reserved.
// Licensed under the MIT License.

using System.Text;

namespace GitDiffWrapper
{
    internal static class ExtensionMethods
    {
        private static readonly char[] NeedsEscaping = new char[] { ' ', '"', '\\' };

        public static string String(this Side self)
        {
            switch (self)
            {
                case Side.Neither: return "Neither";
                case Side.Left: return "Left";
                case Side.Right: return "Right";
                default: return "Unknown";
            }
        }

        public static void AppendEscapedArg(this StringBuilder self, string arg)
        {
            int needsEscaping = arg == null
                ? -1
                : arg.IndexOfAny(NeedsEscaping);
            if (needsEscaping < 0)
            {
                self.Append(arg);
            }
            else
            {
                int equals = arg.IndexOf('=');
                int i = 0;

                if (0 < equals && equals < needsEscaping)
                {
                    self.Append(arg, 0, equals + 1);
                    i = equals + 1;
                }

                self.Append('"');

                int slashes = 0;
                for (; i < arg.Length; i += 1)
                {
                    char ch = arg[i];
                    switch (ch)
                    {
                        case '\\':

                            slashes += 1;
                            break;

                        case '"':

                            if (slashes != 0)
                            {
                                self.Append('\\', slashes * 2);
                            }

                            slashes = 0;
                            self.Append("\\\"");
                            break;

                        default:

                            if (slashes != 0)
                            {
                                self.Append('\\', slashes);
                            }

                            slashes = 0;
                            self.Append(ch);
                            break;
                    }
                }

                if (slashes != 0)
                {
                    self.Append('\\', slashes * 2);
                }

                self.Append('"');
            }
        }
    }
}
