// Copyright (c) Doug Cook. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CultureInfo = System.Globalization.CultureInfo;
using StringBuilder = System.Text.StringBuilder;

namespace GitDiffWrapper
{
    internal enum Side
    {
        Neither,
        Left,
        Right
    }

    internal class ParsedArgs
    {
        private static readonly char[] NeedsEscaping =
            new char[] { ' ', '"', '\\' };

        private const string TempDefault = "%TEMP%";
        private const string GuiToolDefault = "windiff.exe";
        private const string GuiArgsDefault = @"-i $F";
        private const string GitDefault = "git.exe";

        public const string Usage = @"
Usage:

    " + Program.Name + @" [Options] [--] [Paths...]

        With 0 commits, compares the staging area with the working directory.

    " + Program.Name + @" [Options] Commit [--] [Paths...]

        With 1 commit, compares Commit with the working directory.

    " + Program.Name + @" [Options] Commit1 Commit2 [--] [Paths...]

        With 2 commits, compares Commit1 with Commit2.

    " + Program.Name + @" [Options] Commit1..Commit2 [--] [Paths...]

        With a .. commit range, compares Commit1 with Commit2.

    " + Program.Name + @" [Options] Commit1...Commit2 [--] [Paths...]

        With a ... commit range, compares (common ancestor of Commit1 and
        Commit2) against Commit2.

    " + Program.Name + @" [Options] --cached [--] [Paths...]

        With 0 commits and --cached, compares the staging area with HEAD.

    " + Program.Name + @" [Options] --cached Commit [--] [Paths...]

        With 1 commit and --cached, compares the staging area with Commit.

Commit can specify either a real commit or a tree. If Paths... are provided,
only diffs in matching files will be shown. There may be some ambiguity in
distinguishing between Commit and Path names. To eliminate the ambiguity, add
the ""--"" parameter after the last Commit (if any) and before the first Path
(if any).

General usage is the same as ""git diff"" except that:

  - The diffs will be shown in a user-defined diff tool instead of as a patch.
  - Some GitDiffWrapper-specific options are supported to configure how the
    user-defined diff tool is launched.
  - Not all ""git diff"" options are supported.
  - Does not block your command prompt while the diff tool us running.
  - Optionally shows untracked files.
  - Does not show touched but unmodified files.

The following GitDiffWrapper-specific options are supported:

  ...        Shows differences under current path. Same as ""--relative"".
  -u         When diffing against a working directory, look for untracked
             files (default).
  -uno       When diffing against a working directory, don't scan for
             untracked files (overrides -u).
  --tool=... Uses the specified diff tool. Default is: " + GuiToolDefault + @"
  --args=... Uses the specified parameters for the diff tool. The following
             substitutions can be given:
             $$ is replaced with $.
             $1 is replaced with the left-hand path to compare.
             $2 is replaced with the right-hand path to compare.
             $F is replaced with the name of a ""list"" file that contains the
                names of the files to be compared (e.g. windiff -i $F).
             Default is: " + GuiArgsDefault + @"
  --git=...  Uses the specified git program. Default is: " + GitDefault + @"
  --temp=... Uses the specified directory for temp files. Environment
             variables can be used. Default is: " + TempDefault + @"
  --error=.. What to do if an error occurs:
             --error=none   Do nothing and exit the program  (default).
             --error=pause  Wait for a keystroke before exiting.
             --error=msgbox Show a popup window with the error message.
  -v, -q     Makes this program's output verbose (-v) or quiet (-q).
  -h, -?     Shows this help message.

This tool calls ""git diff --raw"" to generate the list of files to compare.
The following git diff options are recognized, and will be passed through to
the git diff command (refer to https://git-scm.com/docs/git-diff for details):

  -B[...]   (break rewrites)
  -C[...]   (find copies)
  -G[...]   (look for changes affecting specified regex)
  -l[...]   (limit rename/copy processing time)
  -M[...]   (find renames)
  -R        (swap left and right sides)
  -S[...]   (look for changes affecting specified string)
  --cached  (compares with index instead of working directory)
  --staged  (same as --cached)
  --break-rewrites[=...]
  --diff-filter[=...]
  --find-copies[=...]
  --find-copies-harder[=...]
  --find-renames[=...]
  --no-renames
  --relative[=...]

Note that all options must start with ""-"", not ""/"".

Typically, you will create a small batch file (e.g. ""gd.bat"") to launch
" + Program.Name + @" with parameters appropriate for your situation (e.g you
might have your batch file set --tool, --args, -uno, --git, etc.).

With no arguments, " + Program.Name + @" will launch windiff.exe. To launch a
tool other than windiff.exe, add the --tool=program.exe parameter:

    @" + Program.Name + @".exe --tool=""sdvdiff.exe"" %*

By default, the tool will be launched as ""ToolName.exe " + GuiArgsDefault + @"
This works great for windiff and sdvdiff, but not all tools support the
""-i list"" parameter (where ""list"" is a the name of a file that contains a
list of filenames to be shown in the diff). For diff tools that don't support
list files, add something like --args=""$1 $2"" which will copy all files into
temporary directories instead of using a list file. For example, a typical
batch file for starting odd.exe might contain the following:

    @" + Program.Name + @".exe --tool=""odd.exe"" --args=""$1 $2"" %*

Hope this helps!
";

        private readonly bool DashDash;
        public readonly bool ArgError;
        public readonly bool Help;
        public readonly bool Cached;
        public readonly bool Reverse;
        public readonly bool Untracked = true;
        public readonly Side WorkingDir;
        public readonly int CleanupParentPid;

        /// <summary>
        /// Set to null means:  --relative
        /// Set to "abc" means: --relative="abc"
        /// Set to "" means:    --relative=""
        ///   which is the same as no --relative parameter at all.
        /// </summary>
        public readonly string Relative = "";
        public readonly string Temp = TempDefault;
        public readonly string GuiTool = GuiToolDefault;
        public readonly string GuiArgs = GuiArgsDefault;
        public readonly string Git = GitDefault;
        public readonly List<string> Options = new List<string>();
        public readonly List<string> Names = new List<string>();
        public readonly List<string> Paths = new List<string>();

        public ParsedArgs(string[] args)
        {
            /*
            This is a very rough approximation for how diff parses args.
            Several cases are unhandled.
            - We don't handle paths at all, e.g. git diff -- path1.c path2.c
            - Probably confused by some revision specs.
            - Probably other stuff.
            */

            foreach (var arg in args)
            {
                if (arg == "...")
                {
                    this.Relative = null;
                }
                else if (this.DashDash)
                {
                    this.Paths.Add(arg);
                }
                else if (arg[0] != '-')
                {
                    if (arg == "/h" ||
                        arg == "/H" ||
                        arg == "/?")
                    {
                        this.Help = true;
                    }
                    else
                    {
                        this.Names.Add(arg);
                    }
                }
                else if (arg.Length < 2 || arg[1] != '-')
                {
                    // Single-dash
                    if (arg.StartsWith("-h", StringComparison.OrdinalIgnoreCase) ||
                        arg.StartsWith("-?", StringComparison.Ordinal))
                    {
                        this.Help = true;
                    }
                    else if (arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Level = System.Diagnostics.TraceLevel.Verbose;
                    }
                    else if (arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Level = System.Diagnostics.TraceLevel.Warning;
                    }
                    else if (arg.Equals("-u", StringComparison.OrdinalIgnoreCase))
                    {
                        this.Untracked = true;
                    }
                    else if (arg.Equals("-uno", StringComparison.OrdinalIgnoreCase))
                    {
                        this.Untracked = false;
                    }
                    else if (arg.Equals("-R", StringComparison.Ordinal))
                    {
                        this.Reverse = true;
                    }
                    else if (
                        arg.StartsWith("-M", StringComparison.Ordinal) ||
                        arg.StartsWith("-C", StringComparison.Ordinal) ||
                        arg.StartsWith("-l", StringComparison.Ordinal) ||
                        arg.StartsWith("-S", StringComparison.Ordinal) ||
                        arg.StartsWith("-G", StringComparison.Ordinal) ||
                        arg.StartsWith("-b", StringComparison.Ordinal))
                    {
                        this.Options.Add(arg);
                    }
                    else
                    {
                        Logger.Warn(
                            "ignoring unsupported/unrecognized option \"{0}\".",
                            arg);
                    }
                }
                else if (arg.Length == 2)
                {
                    this.DashDash = true;
                }
                else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
                {
                    this.Help = true;
                }
                else if (arg.StartsWith("--error=", StringComparison.OrdinalIgnoreCase))
                {
                    string error = arg.Substring(arg.IndexOf('=') + 1);
                    if (error.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        Program.ErrorAction = ErrorAction.None;
                    }
                    else if (error.Equals("pause", StringComparison.OrdinalIgnoreCase))
                    {
                        Program.ErrorAction = ErrorAction.Pause;
                    }
                    else if (
                        error.Equals("msgbox", StringComparison.OrdinalIgnoreCase) ||
                        error.Equals("messagebox", StringComparison.OrdinalIgnoreCase))
                    {
                        Program.ErrorAction = ErrorAction.MessageBox;
                    }
                    else
                    {
                        Logger.Warn(
                            "ignoring unrecognized option \"{0}\" (valid error actions are none, pause, msgbox).",
                            arg);

                    }
                }
                else if (
                    arg.Equals("--staged", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--cached", StringComparison.OrdinalIgnoreCase))
                {
                    this.Cached = true;
                }
                else if (arg.Equals("--relative", StringComparison.OrdinalIgnoreCase))
                {
                    this.Relative = null;
                }
                else if (arg.StartsWith("--relative=", StringComparison.OrdinalIgnoreCase))
                {
                    this.Relative = arg.Substring(arg.IndexOf('=') + 1);
                }
                else if (arg.StartsWith("--CleanupParentPid=", StringComparison.OrdinalIgnoreCase))
                {
                    this.CleanupParentPid = int.Parse(
                        arg.Substring(arg.IndexOf('=') + 1),
                        CultureInfo.InvariantCulture);
                }
                else if (arg.StartsWith("--temp=", StringComparison.OrdinalIgnoreCase))
                {
                    this.Temp = arg.Substring(arg.IndexOf('=') + 1);
                }
                else if (arg.StartsWith("--tool=", StringComparison.OrdinalIgnoreCase))
                {
                    this.GuiTool = arg.Substring(arg.IndexOf('=') + 1);
                }
                else if (arg.StartsWith("--args=", StringComparison.OrdinalIgnoreCase))
                {
                    this.GuiArgs = arg.Substring(arg.IndexOf('=') + 1);
                }
                else if (arg.StartsWith("--git=", StringComparison.OrdinalIgnoreCase))
                {
                    this.Git = arg.Substring(arg.IndexOf('=') + 1);
                }
                else if (
                    arg.Equals("--no-renames", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--break-rewrites", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--find-renames", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--find-copies", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--find-copies-harder", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--diff-filter", StringComparison.OrdinalIgnoreCase))
                {
                    this.Options.Add(arg);
                }
                else
                {
                    Logger.Warn(
                        "ignoring unsupported/unrecognized option \"{0}\".",
                        arg);
                }
            }

            if (!this.DashDash)
            {
                for (int i = 0; i != this.Names.Count; i += 1)
                {
                    string name = this.Names[i];
                    bool isObject = this.IsObject(name);
                    bool isPath = File.Exists(name) || Directory.Exists(name);
                    if (isObject == isPath)
                    {
                        if (isObject)
                        {
                            Logger.Error(
                                "ambiguous argument '{0}': both revision and filename",
                                name);
                        }
                        else
                        {
                            Logger.Error(
                                "ambiguous argument '{0}': unknown revision or path not in the working tree.",
                                name);
                        }

                        Logger.Error("Use '--' to separate paths from revisions, like this:");
                        Logger.Error("'git <command> [<revision>...] -- [<file>...]'");
                        this.ArgError = true;
                        return;
                    }

                    if (isPath)
                    {
                        Logger.Verbose("Assuming argument is a path: {0}", name);
                        for (int j = this.Names.Count; i != j; j -= 1)
                        {
                            this.Paths.Insert(0, this.Names[j - 1]);
                            this.Names.RemoveAt(j - 1);
                        }
                        break;
                    }
                    else
                    {
                        Logger.Verbose("Assuming argument is an object: {0}", name);
                    }
                }
            }

            int nameCount = this.Names.Count;
            for (int i = nameCount; i != 0; i -= 1)
            {
                if (this.Names[i - 1].Contains(".."))
                {
                    nameCount += 1;
                }
            }

            int cachedCount = this.Cached ? 1 : 0;
            int nameLimit = 2 - cachedCount;
            if (nameCount > nameLimit)
            {
                Logger.Error(
                    "Too many <commit> parameters. Found {0}. Expected no more than {1}. Giving up.",
                    nameCount,
                    nameLimit);
                this.ArgError = true;
                return;
            }

            bool useWorkingDir = this.Cached
                ? false
                : nameCount < 2;
            this.WorkingDir = !useWorkingDir
                ? Side.Neither
                : this.Reverse
                ? Side.Left
                : Side.Right;

            this.Temp = Environment.ExpandEnvironmentVariables(this.Temp);
        }

        public string DiffArgs()
        {
            var sb = new StringBuilder();

            if (this.Relative == null)
            {
                AppendRaw(sb, "--relative");
            }
            else if (this.Relative.Length != 0)
            {
                Append(sb, "--relative=" + this.Relative);
            }

            if (this.Reverse)
            {
                AppendRaw(sb, "-R");
            }

            foreach (var arg in this.Options)
            {
                Append(sb, arg);
            }

            if (this.Cached)
            {
                AppendRaw(sb, "--cached");
            }

            foreach (var arg in this.Names)
            {
                Append(sb, arg);
            }

            AppendRaw(sb, "--");

            if (this.Paths.Count != 0)
            {
                foreach (var arg in this.Paths)
                {
                    Append(sb, arg);
                }
            }

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string arg)
        {
            sb.Append(' ');
            sb.AppendEscapedArg(arg);
        }

        private static void AppendRaw(StringBuilder sb, string arg)
        {
            sb.Append(' ');
            sb.Append(arg);
        }

        private bool IsObject(string name)
        {
            bool isObject;
            int i = name.IndexOf("..");
            if (i < 0)
            {
                isObject = IsObjectImpl(name);
            }
            else
            {
                string name1 = name.Substring(0, i);
                string name2 = name.Length > i + 2 && name[i + 2] == '.'
                    ? name.Substring(i + 3, name.Length - i - 3)
                    : name.Substring(i + 2, name.Length - i - 2);
                if (name1.Length != 0)
                {
                    if (IsObjectImpl(name1))
                    {
                        isObject = name2.Length == 0 || IsObjectImpl(name2);
                    }
                    else
                    {
                        isObject = false;
                    }
                }
                else if (name2.Length != 0)
                {
                    isObject = IsObjectImpl(name2);
                }
                else
                {
                    isObject = false;
                }
            }

            return isObject;
        }

        private bool IsObjectImpl(string name)
        {
            var args = new StringBuilder(26 + name.Length);
            args.Append("rev-parse --verify -q ");
            args.AppendEscapedArg(name);
            var gitRevParsePsi = new ProcessStartInfo(
                this.Git,
                args.ToString())
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            Logger.Verbose("git launch: {0} {1}", gitRevParsePsi.FileName, gitRevParsePsi.Arguments);
            using (var git = Process.Start(gitRevParsePsi))
            {
                git.StandardOutput.ReadToEnd();
                git.WaitForExit();
                Logger.Verbose("git return: {0}", git.ExitCode.ToString(CultureInfo.InvariantCulture));
                return git.ExitCode == 0;
            }
        }
    }
}
