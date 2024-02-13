// Copyright (c) Doug Cook. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CultureInfo = System.Globalization.CultureInfo;

[assembly: AssemblyVersion("1.0.*")]

namespace GitDiffWrapper
{
    internal enum ErrorAction
    {
        None,
        Pause,
        MessageBox
    }

    internal static class Program
    {
        public const string Name = "GitDiffWrapper";
        public static ErrorAction ErrorAction;

        private const string ToolPidFileName = "toolPid";
        private const string CleanupFileName = "cleanup";
        private const string LockFileName = "lock";
        private const string DirSuffix1 = "left";
        private const string DirSuffix2 = "right";
        private const string SummaryFileName = DirSuffix2 + "\\ summary";
        private const string ListFileName = "list";

        public static int Main(string[] args)
        {
            int result;

            if (Debugger.IsAttached)
            {
                result = Run(args);
            }
            else
            {
                try
                {
                    result = Run(args);
                }
                catch (Exception ex)
                {
                    Logger.Error("{0}: {1}", ex.GetType().Name, ex.Message);
                    result = 1;
                }
            }

            if (result != 0)
            {
                DoErrorAction();
            }

            return result;
        }

        public static int Run(string[] args)
        {
            string directoryToDelete = null;
            try
            {
                string tempDir;
                var parsedArgs = new ParsedArgs(args);
                if (parsedArgs.Help)
                {
                    Logger.Write(ParsedArgs.Usage);
                    Logger.Write(
                        Program.Name + " v{0} by dcook{1}",
                        typeof(Program).Assembly.GetName().Version.ToString(),
                        Environment.NewLine);
                    return 1;
                }
                else if (parsedArgs.ArgError)
                {
                    return 1;
                }
                else if (parsedArgs.CleanupParentPid != 0)
                {
                    Logger.Verbose(
                        "CLEANUP: start: {0} {1}",
                        parsedArgs.CleanupParentPid,
                        parsedArgs.Temp);

                    tempDir = parsedArgs.Temp;
                    var lockFileName = Path.Combine(tempDir, LockFileName);
                    if (!File.Exists(lockFileName))
                    {
                        Logger.Error("CLEANUP: exiting (no lock)");
                        return 1;
                    }

                    directoryToDelete = tempDir;

                    try
                    {
                        File.WriteAllText(
                            Path.Combine(tempDir, CleanupFileName),
                            "");
                        using (var parent = Process.GetProcessById(parsedArgs.CleanupParentPid))
                        {
                            if (parent != null)
                            {
                                parent.WaitForExit();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("CLEANUP: exception (parent): {0}", ex.Message);
                    }

                    string toolPidFileName = Path.Combine(tempDir, ToolPidFileName);
                    if (File.Exists(toolPidFileName))
                    {
                        try
                        {
                            var toolPid = int.Parse(
                                File.ReadAllText(toolPidFileName),
                                CultureInfo.InvariantCulture);
                            using (var tool = Process.GetProcessById(toolPid))
                            {
                                tool.WaitForExit();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("CLEANUP: exception (tool): {0}", ex.Message);
                        }
                    }

                    Logger.Verbose("CLEANUP: exiting (done)");
                    return 0;
                }

                string currentDirectory = Directory.GetCurrentDirectory();
                string gitRoot = currentDirectory;
                while (
                    gitRoot != null &&
                    !Directory.Exists(Path.Combine(gitRoot, ".git")))
                {
                    gitRoot = Path.GetDirectoryName(gitRoot);
                }

                if (gitRoot == null || !Directory.Exists(gitRoot))
                {
                    Logger.Error(
                        "Unable to find GitRoot (search started at \"{0}\").",
                        currentDirectory);
                    return 1;
                }

                Logger.Verbose(
                    "TempDir = {0}",
                    parsedArgs.Temp);
                Logger.Verbose(
                    "GuiTool = \"{0}\" {1}",
                    parsedArgs.GuiTool,
                    parsedArgs.GuiArgs);
                Logger.Verbose(
                    "GitRoot = {0}",
                    gitRoot);

                if (parsedArgs.WorkingDir != Side.Neither)
                {
                    Logger.Info(
                        "WorkingDir = {0}",
                        parsedArgs.WorkingDir.String());
                }

                tempDir = CreateTempDir(parsedArgs.Temp);
                directoryToDelete = tempDir;

                {
                    var commandLineBuilder = new StringBuilder();
                    commandLineBuilder.AppendEscapedArg(typeof(Program).Assembly.Location);
                    commandLineBuilder.Append(" --CleanupParentPid=");
                    commandLineBuilder.Append(NativeMethods.GetCurrentProcessId().ToString(CultureInfo.InvariantCulture));
                    commandLineBuilder.Append(" --temp=");
                    commandLineBuilder.AppendEscapedArg(tempDir);

                    var commandLine = commandLineBuilder.ToString();
                    Logger.Verbose("CLEANUP launch: {0}", commandLine);

                    int startOk = 0;
                    var processInformation = new ProcessInformation();
                    try
                    {
                        var startupInfo = new StartupInfo() {
                            cb = Marshal.SizeOf(typeof(StartupInfo))
                        };
                        startOk = NativeMethods.CreateProcessW(
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            0,
                            CreationFlags.CreateNewProcessGroup |
                            CreationFlags.CreateNoWindow |
                            CreationFlags.DetachedProcess |
                            CreationFlags.None,
                            IntPtr.Zero,
                            null,
                            ref startupInfo,
                            out processInformation);
                    }
                    finally
                    {
                        if (processInformation.ProcessHandle != IntPtr.Zero)
                        {
                            NativeMethods.CloseHandle(processInformation.ProcessHandle);
                        }

                        if (processInformation.ThreadHandle != IntPtr.Zero)
                        {
                            NativeMethods.CloseHandle(processInformation.ThreadHandle);
                        }
                    }

                    if (startOk == 0)
                    {
                        Logger.Error(
                            "Failed to launch cleanup process: {0}",
                            Marshal.GetLastWin32Error());
                        return 1;
                    }

                    directoryToDelete = null;
                }

                bool useListFile;
                var guiToolArgs = GenerateGuiToolArgs(parsedArgs.GuiArgs, out useListFile);
                if (guiToolArgs == null)
                {
                    return 1;
                }

                var inputDir =
                    parsedArgs.Relative == null
                    ? currentDirectory
                    : parsedArgs.Relative.Length == 0
                    ? gitRoot
                    : Path.Combine(gitRoot, parsedArgs.Relative.Replace('/', '\\'));

                var gitDiffArgs = parsedArgs.DiffArgs();
                var diffRecords = RunGitDiff(parsedArgs, gitDiffArgs, inputDir);
                if (diffRecords == null)
                {
                    return 1;
                }

                Logger.Verbose("Generating compare directories...");

                var dir1 = Path.Combine(tempDir, DirSuffix1);
                var dir2 = Path.Combine(tempDir, DirSuffix2);
                var listFilePath = Path.Combine(tempDir, ListFileName);
                var summaryFilePath = Path.Combine(tempDir, SummaryFileName);
                Directory.CreateDirectory(dir1);
                Directory.CreateDirectory(dir2);
                using (StreamWriter
                    listFile = new StreamWriter(listFilePath, false, Encoding.Default),
                    summaryFile = new StreamWriter(summaryFilePath, false, Encoding.Default))
                {
                    listFile.WriteLine(
                        @"""{0}"" ""{0}""",
                        SummaryFileName);

                    summaryFile.WriteLine(
                        "Root: {0}",
                        inputDir);
                    summaryFile.WriteLine(
                        "Diff: {0} diff{1}",
                        parsedArgs.Git,
                        gitDiffArgs);
                    summaryFile.WriteLine();
                    summaryFile.WriteLine(
                        "{0} changed file(s)",
                        diffRecords.Length.ToString(CultureInfo.InvariantCulture));
                    summaryFile.WriteLine();

                    var gitCatPsi = new ProcessStartInfo(
                        parsedArgs.Git,
                        "cat-file --batch=Z%(objectsize)")
                    {
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    };
                    Logger.Verbose("git launch: {0} {1}", gitCatPsi.FileName, gitCatPsi.Arguments);
                    using (var git = Process.Start(gitCatPsi))
                    {
                        var buffer = new byte[65536];
                        foreach (var rec in diffRecords)
                        {
                            if (rec.SrcPath == rec.DstPath &&
                                parsedArgs.WorkingDir != Side.Neither)
                            {
                                string hash1 = parsedArgs.WorkingDir == Side.Left ? rec.SrcHash : rec.DstHash;
                                string hash2 = parsedArgs.WorkingDir == Side.Left ? rec.DstHash : rec.SrcHash;
                                if (hash1 == "0" &&
                                    hash2 != "0" &&
                                    HashMatch(buffer, hash2, Path.Combine(inputDir, rec.SrcPath)))
                                {
                                    Logger.Verbose("Skipping unchanged file: {0} ({1})", rec.SrcPath, hash2);
                                    continue;
                                }
                            }

                            summaryFile.WriteLine(
                                rec.SrcPath == rec.DstPath
                                ? "{0} {1} ({2} ==> {3})"
                                : "{0} {1} ==> {4} ({2} ==> {3})",
                                rec.Status.ToString(),
                                rec.SrcPath,
                                rec.SrcHash,
                                rec.DstHash,
                                rec.DstPath);
                            PlaceFile(
                                git,
                                buffer,
                                listFile,
                                parsedArgs.WorkingDir == Side.Left ? inputDir : null,
                                dir1,
                                rec.SrcPath,
                                rec.SrcHash,
                                useListFile);
                            listFile.Write(' ');
                            PlaceFile(
                                git,
                                buffer,
                                listFile,
                                parsedArgs.WorkingDir == Side.Right ? inputDir : null,
                                dir2,
                                rec.DstPath,
                                rec.DstHash,
                                useListFile);
                            listFile.WriteLine();
                        }

                        git.StandardInput.Close();
                        git.WaitForExit();
                        if (git.ExitCode == 0)
                        {
                            Logger.Verbose("git return: {0}", git.ExitCode.ToString(CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            Logger.Error("git return: {0}", git.ExitCode.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                StartGuiTool(tempDir, parsedArgs.GuiTool, guiToolArgs);
                return 0;
            }
            finally
            {
                DeleteTempDir(directoryToDelete);
            }
        }

        private static bool HashMatch(
            byte[] buffer,
            string existingHash,
            string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }

            using (var file = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                var size = file.Length;
                string header = "blob " + size.ToString(CultureInfo.InvariantCulture) + "\0";
                int headerSize = Encoding.UTF8.GetBytes(header, 0, header.Length, buffer, 0);
                using (var sha1 = SHA1.Create())
                {
                    sha1.TransformBlock(buffer, 0, headerSize, buffer, 0);

                    while (size != 0)
                    {
                        var readSize = size > (uint)buffer.Length
                            ? buffer.Length
                            : (int)size;
                        var c = file.Read(buffer, 0, readSize);
                        if (c == 0)
                        {
                            Logger.Error("Unexpected end-of-output from 'git cat-file'");
                            throw new InvalidOperationException();
                        }

                        sha1.TransformBlock(buffer, 0, c, buffer, 0);
                        size -= (uint)c;
                    }

                    sha1.TransformFinalBlock(buffer, 0, 0);

                    var newHashBytes = sha1.Hash;
                    char ch1, ch2;
                    for (int i = 0; i < newHashBytes.Length; i++)
                    {
                        if (i > existingHash.Length / 2)
                        {
                            return true;
                        }

                        ch1 = char.ToLowerInvariant(existingHash[i * 2 + 0]);
                        ch2 = HexChar((uint)newHashBytes[i] >> 4);
                        if (ch1 != ch2)
                        {
                            return false;
                        }

                        ch1 = char.ToLowerInvariant(existingHash[i * 2 + 1]);
                        ch2 = HexChar((uint)newHashBytes[i] & 0xf);
                        if (ch1 != ch2)
                        {
                            return false;
                        }
                    }

                    return existingHash.Length <= newHashBytes.Length * 2;
                }
            }
        }

        private static char HexChar(uint n)
        {
            return n < 10 ? (char)(n + '0') : (char)(n - 10 + 'a');
        }

        private static void PlaceFile(
            Process git,
            byte[] buffer,
            StreamWriter listFile,
            string inputDir,
            string compareDir,
            string filePath,
            string fileHash,
            bool useListFile)
        {
            var compareDirPath = Path.Combine(compareDir, filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(compareDirPath));
            if (inputDir != null)
            {
                var inputDirPath = Path.Combine(inputDir, filePath);
                listFile.Write(@"""{0}""", inputDirPath);
                if (!useListFile &&
                    File.Exists(inputDirPath) &&
                    !File.Exists(compareDirPath))
                {
                    Logger.Info("FileCopy: {0}", filePath);
                    File.Copy(inputDirPath, compareDirPath, false);
                }
            }
            else
            {
                listFile.Write(@"""{0}""", Path.Combine(Path.GetFileName(compareDir), filePath));
                if (fileHash != "0" &&
                    !File.Exists(compareDirPath))
                {
                    Logger.Info("cat-file: {0} ({1})", filePath, fileHash);
                    GitCatFile(git, buffer, fileHash, compareDirPath);
                }
            }
        }

        private static void GitCatFile(Process git, byte[] buffer, string hash, string filePath)
        {
            git.StandardInput.Write("{0}\n", hash);
            ulong size = 0;
            int ch;
            ch = git.StandardOutput.BaseStream.ReadByte();

            if (ch != 'Z')
            {
                var sb = new StringBuilder();
                for (;;)
                {
                    if (ch < 0)
                    {
                        Logger.Error("Unexpected end-of-output from 'git cat-file'");
                        throw new InvalidOperationException();
                    }
                    else if (ch == '\n')
                    {
                        Logger.Warn("cat-file failed: {0}", sb.ToString());
                        using (var file = new StreamWriter(filePath))
                        {
                            file.WriteLine("cat-file failed: {0}", sb.ToString());
                        }
                        return;
                    }

                    sb.Append((char)ch);
                    ch = git.StandardOutput.BaseStream.ReadByte();
                }
            }
            else
            {
                for (;;)
                {
                    ch = git.StandardOutput.BaseStream.ReadByte();
                    if (ch < '0' || '9' < ch)
                    {
                        break;
                    }

                    size = size * 10 + (uint)(ch - '0');
                }

                if (ch != '\n')
                {
                    Logger.Error("Unexpected pre-blob output from 'git cat-file': {0}", ch);
                    throw new InvalidOperationException();
                }
            }

            using (var file = new FileStream(filePath, FileMode.CreateNew))
            {
                while (size != 0)
                {
                    var readSize = size > (uint)buffer.Length
                        ? buffer.Length
                        : (int)size;
                    var c = git.StandardOutput.BaseStream.Read(buffer, 0, readSize);
                    if (c == 0)
                    {
                        Logger.Error("Unexpected end-of-output from 'git cat-file'");
                        throw new InvalidOperationException();
                    }

                    file.Write(buffer, 0, c);
                    size -= (uint)c;
                }
            }

            ch = git.StandardOutput.BaseStream.ReadByte();

            if (ch != '\n')
            {
                Logger.Error("Unexpected post-blob output from 'git cat-file': {0}", ch);
                throw new InvalidOperationException();
            }
        }

        private static string GenerateGuiToolArgs(string args, out bool useListFile)
        {
            useListFile = false;
            var toolArgsBuilder = new StringBuilder(
                args.Length + DirSuffix1.Length + DirSuffix2.Length);
            for (int i = 0; i < args.Length; i += 1)
            {
                var ch = args[i];
                if (ch != '$')
                {
                    toolArgsBuilder.Append(ch);
                }
                else if (i + 1 == args.Length)
                {
                    Logger.Error("Invalid escape sequence in --args: $");
                    return null;
                }
                else
                {
                    i += 1;
                    ch = args[i];
                    switch (ch)
                    {
                        case '$':
                            toolArgsBuilder.Append(ch);
                            break;
                        case '1':
                            toolArgsBuilder.Append(DirSuffix1);
                            break;
                        case '2':
                            toolArgsBuilder.Append(DirSuffix2);
                            break;
                        case 'F':
                        case 'f':
                            toolArgsBuilder.Append(ListFileName);
                            useListFile = true;
                            break;
                        default:
                            Logger.Error("Invalid escape sequence in --args: ${0}", ch.ToString());
                            return null;
                    }
                }
            }

            return toolArgsBuilder.ToString();
        }

        private static DiffRecord[] RunGitDiff(ParsedArgs parsedArgs, string gitDiffArgs, string inputDir)
        {
            var gitDiffRawArgs = "diff --raw --abbrev=64" + gitDiffArgs;
            var diffRecords = new List<DiffRecord>();
            Process gitLsFiles = null;
            Process gitDiff = null;
            try
            {
                var gitDiffPsi = new ProcessStartInfo(
                    parsedArgs.Git,
                    gitDiffRawArgs)
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                };
                Logger.Info("{0} {1}", gitDiffPsi.FileName, gitDiffPsi.Arguments);
                gitDiff = Process.Start(gitDiffPsi);
                gitDiff.OutputDataReceived +=
                    delegate (object sender, DataReceivedEventArgs d)
                    {
                        if (!string.IsNullOrEmpty(d.Data))
                        {
                            if (diffRecords.Count == 0)
                            {
                                Logger.Verbose("receiving data from git...");
                            }

                            var record = new DiffRecord(d.Data);
                            lock (diffRecords)
                            {
                                diffRecords.Add(record);
                            }
                        }
                    };
                gitDiff.BeginOutputReadLine();

                if (parsedArgs.Untracked && parsedArgs.WorkingDir != Side.Neither)
                {
                    var gitLsFilesPsi = new ProcessStartInfo(
                        parsedArgs.Git,
                        "ls-files --others --exclude-standard")
                    {
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        UseShellExecute = false,
                        WorkingDirectory = inputDir,
                    };
                    Logger.Info("{0} {1}", gitLsFilesPsi.FileName, gitLsFilesPsi.Arguments);
                    gitLsFiles = Process.Start(gitLsFilesPsi);
                    gitLsFiles.OutputDataReceived +=
                        delegate (object sender, DataReceivedEventArgs d)
                        {
                            if (!string.IsNullOrEmpty(d.Data))
                            {
                                var record = new DiffRecord(d.Data.Replace('/', '\\'), parsedArgs.WorkingDir);
                                if (record.Status == 'U' &&
                                    record.SrcHash == "0" &&
                                    record.DstHash == "0")
                                {
                                    // The Unmerged records are dupes of the Modified records except with
                                    // SrcHash and DstHash set to 0. They seem useless, so skip them.
                                    // There might be some useful Unmerged records, so only skip the ones
                                    // with "0" hashes.
                                }
                                else
                                {
                                    lock (diffRecords)
                                    {
                                        diffRecords.Add(record);
                                    }
                                }
                            }
                        };
                    gitLsFiles.BeginOutputReadLine();

                }

                gitDiff.WaitForExit();
                if (gitDiff.ExitCode == 0)
                {
                    Logger.Verbose("git return: {0}", gitDiff.ExitCode.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    Logger.Error("git return: {0}", gitDiff.ExitCode.ToString(CultureInfo.InvariantCulture));
                    return null;
                }

                if (gitLsFiles != null)
                {
                    gitLsFiles.WaitForExit();
                    if (gitLsFiles.ExitCode == 0)
                    {
                        Logger.Verbose("git ls-files return: {0}", gitLsFiles.ExitCode.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Logger.Error("git ls-files return: {0}", gitLsFiles.ExitCode.ToString(CultureInfo.InvariantCulture));
                        return null;
                    }
                }
            }
            finally
            {
                if (gitLsFiles != null)
                {
                    gitLsFiles.Dispose();
                }

                if (gitDiff != null)
                {
                    gitDiff.Dispose();
                }
            }

            var array = diffRecords.ToArray();
            Array.Sort(array, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.SrcPath, b.SrcPath));
            return array;
        }

        private static void StartGuiTool(
            string workingDirectory,
            string guiToolProgram,
            string guiToolArgs)
        {
            var guiToolPsi = new ProcessStartInfo(guiToolProgram, guiToolArgs)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            Logger.Info("{0} {1}", guiToolPsi.FileName, guiToolPsi.Arguments);
            using (var tool = Process.Start(guiToolPsi))
            {
                string toolPid = Path.Combine(workingDirectory, ToolPidFileName);
                File.WriteAllText(toolPid, tool.Id.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string CreateTempDir(string tempRoot)
        {
            for (uint i = unchecked((uint)DateTime.UtcNow.Ticks); ; i += 1)
            {
                string tempDir = Path.Combine(
                    tempRoot,
                    Program.Name + "_" + i.ToString("x8", CultureInfo.InvariantCulture));
                if (!Directory.Exists(tempDir))
                {
                    Logger.Verbose("Trying temp dir: {0}", tempDir);
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        using (var fs = new FileStream(
                            Path.Combine(tempDir, LockFileName),
                            FileMode.CreateNew))
                        {
                            Logger.Verbose("Created temp dir: {0}", tempDir);
                            return tempDir;
                        }
                    }
                    catch (IOException)
                    {
                        // Continue.
                    }
                }
            }
        }

        private static void DeleteTempDir(string tempDir)
        {
            if (tempDir != null)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Logger.Verbose("Removing temp dir: {0}", tempDir);
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to clean up temp dir \"{0}\"", tempDir);
                    Logger.Error("{0}: {1}", ex.GetType().Name, ex.Message);
                }
            }
        }

        private static void DoErrorAction()
        {
            try
            {
                switch (ErrorAction)
                {
                    case ErrorAction.Pause:
                        var dummy = Console.KeyAvailable; // Throws if input redirected.
                        Console.Error.WriteLine("Press any key to exit.");
                        Console.Beep();
                        Console.ReadKey();
                        break;
                    case ErrorAction.MessageBox:
                        NativeMethods.MessageBoxW(
                            IntPtr.Zero,
                            Logger.Writer.ToString(),
                            "GitDiffWrapper error",
                            MB.IconWarning | MB.ModalTask);
                        break;
                }
            }
            catch (Exception)
            {
                // Do nothing.
            }
        }

        [Flags]
        private enum MB
        {
            None = 0,
            IconWarning = 0x30,
            ModalSystem = 0x1000,
            ModalTask = 0x2000
        }

        [Flags]
        private enum CreationFlags
        {
            None = 0,
            CreateNewProcessGroup = 0x00000200,
            CreateNoWindow = 0x08000000,
            DetachedProcess = 0x00000008,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr ProcessHandle;
            public IntPtr ThreadHandle;
            public int ProcessId;
            public int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        private static class NativeMethods
        {
            [DllImport(
                "user32.dll",
                CallingConvention = CallingConvention.Winapi,
                CharSet = CharSet.Unicode,
                ExactSpelling = true)]
            public static extern int MessageBoxW(
                IntPtr hwnd,
                string text,
                string caption,
                MB type);

            [DllImport(
                "kernel32.dll",
                CallingConvention = CallingConvention.Winapi,
                CharSet = CharSet.Unicode,
                ExactSpelling = true,
                SetLastError = true)]
            public static extern int CreateProcessW(
                string dummy,
                string commandLine,
                IntPtr processAttributes,
                IntPtr threadAttributes,
                int inheritHandles,
                CreationFlags creationFlags,
                IntPtr environment,
                string currentDirectory,
                ref StartupInfo startupInfo,
                out ProcessInformation processInformation);

            [DllImport(
                "kernel32.dll",
                CallingConvention = CallingConvention.Winapi,
                CharSet = CharSet.Unicode,
                ExactSpelling = true)]
            public static extern int GetCurrentProcessId();

            [DllImport(
                "kernel32.dll",
                CallingConvention = CallingConvention.Winapi,
                CharSet = CharSet.Unicode,
                ExactSpelling = true)]
            public static extern int CloseHandle(IntPtr handle);
        }
    }
}
