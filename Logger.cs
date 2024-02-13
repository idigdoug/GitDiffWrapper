// Copyright (c) Doug Cook. All rights reserved.
// Licensed under the MIT License.

using System;
using CultureInfo = System.Globalization.CultureInfo;
using StringWriter = System.IO.StringWriter;
using TraceLevel = System.Diagnostics.TraceLevel;

namespace GitDiffWrapper
{
    internal static class Logger
    {
        private const string ErrorPrefix = "gd ERROR: ";
        private const string WarningPrefix = "gd WARNING: ";
        private const string InfoPrefix = "gd Info: ";
        private const string VerbosePrefix = "gd verbose: ";
        private const string UnknownPrefix = "gd unk";
        private static StringWriter writer;
        public static TraceLevel Level = TraceLevel.Info;

        public static StringWriter Writer
        {
            get
            {
                if (writer == null)
                {
                    writer = new StringWriter(CultureInfo.InvariantCulture);
                }

                return writer;
            }
        }

        public static string LevelPrefix(TraceLevel messageLevel)
        {
            switch (messageLevel)
            {
                case TraceLevel.Error: return ErrorPrefix;
                case TraceLevel.Warning: return WarningPrefix;
                case TraceLevel.Info: return InfoPrefix;
                case TraceLevel.Verbose: return VerbosePrefix;
                default: return UnknownPrefix + ((int)messageLevel).ToString(CultureInfo.InvariantCulture) + ": ";
            }
        }

        public static void Write(string message)
        {
            Console.Error.Write(message);
        }

        public static void Write(string format, params object[] args)
        {
            Console.Error.Write(format, args);
        }

        public static void WriteLine(string message)
        {
            Console.Error.WriteLine(message);
        }

        public static void WriteLine(string format, params object[] args)
        {
            Console.Error.WriteLine(format, args);
        }

        public static void Message(TraceLevel messageLevel, string message)
        {
            if (Level >= messageLevel)
            {
                Write(LevelPrefix(messageLevel));
                WriteLine(message);
            }
        }

        public static void Message(TraceLevel messageLevel, string format, params object[] args)
        {
            if (Level >= messageLevel)
            {
                Write(LevelPrefix(messageLevel));
                WriteLine(format, args);
            }
        }

        public static void Error(string message)
        {
            if (Level >= TraceLevel.Error)
            {
                Write(ErrorPrefix);
                WriteLine(message);
            }

            var w = Writer;
            w.Write(ErrorPrefix);
            w.WriteLine(message);
        }

        public static void Error(string format, params object[] args)
        {
            if (Level >= TraceLevel.Error)
            {
                Write(ErrorPrefix);
                WriteLine(format, args);
            }

            var w = Writer;
            w.Write(ErrorPrefix);
            w.WriteLine(format, args);
        }

        public static void Warn(string message)
        {
            if (Level >= TraceLevel.Warning)
            {
                Write(WarningPrefix);
                WriteLine(message);
            }

            var w = Writer;
            w.Write(WarningPrefix);
            w.WriteLine(message);
        }

        public static void Warn(string format, params object[] args)
        {
            if (Level >= TraceLevel.Warning)
            {
                Write(WarningPrefix);
                WriteLine(format, args);
            }

            var w = Writer;
            w.Write(WarningPrefix);
            w.WriteLine(format, args);
        }

        public static void Info(string message)
        {
            if (Level >= TraceLevel.Info)
            {
                Write(InfoPrefix);
                WriteLine(message);
            }
        }

        public static void Info(string format, params object[] args)
        {
            if (Level >= TraceLevel.Info)
            {
                Write(InfoPrefix);
                WriteLine(format, args);
            }
        }

        public static void Verbose(string message)
        {
            if (Level >= TraceLevel.Verbose)
            {
                Write(VerbosePrefix);
                WriteLine(message);
            }
        }

        public static void Verbose(string format, params object[] args)
        {
            if (Level >= TraceLevel.Verbose)
            {
                Write(VerbosePrefix);
                WriteLine(format, args);
            }
        }
    }
}
