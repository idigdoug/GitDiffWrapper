# Introduction

GitDiffWrapper helps GUI diff tools (e.g. windiff, Beyond Compare) work with
GIT repositories. `GitDiffWrapper ...` works a lot like `git difftool -d ...`,
 but GitDiffWrapper has a few important differences:

1. GitDiffWrapper optionally generates a windiff-compatible file list instead
   of just pointing the GUI diff tool at two temporary directories. This
   allows the diff tool to operate directly against the file in your working
   directory instead of a copy of the file so that when you edit the right-hand
   file in the diff, your edits will be applied to your working directory. It
   also allows for comparison of renamed/moved files. (If your GUI diff tool
   doesn't support list files, GitDiffWrapper can still copy the files into
   the temporary directory.)
2. GitDiffWrapper doesn't block your build window while you are viewing the
   diff.
3. GitDiffWrapper can optionally include untracked files in the diff.
4. GitDiffWrapper will not show unchanged files even if they've been touched.
   (This is important if .gitconfig option diff.autoRefreshIndex = false.)
5. GitDiffWrapper seems to parse commits and paths better than `git difftool -d`.
5. GitDiffWrapper seems to work with GVFS more reliably than `git difftool -d`.

You'll typically create a script (batch file) to launch GitDiffWrapper with
parameters appropriate for your GUI diff tool. A sample `gd.bat` script is
included with GitDiffWrapper. Run `gd -h` for details on the
parameters supported.

# Quick Start

1. Copy the GetDiffWrapper.exe and gd.bat files to a folder in your path.
2. Customize the sample gd.bat script for use with your GUI diff tool. Run
   `GitDiffWrapper -h` for details on the supported parameters.
3. Use `gd <args>` instead of `git difftool -d <args>`.

# Updates

## 2024/02/13 v1.0.8809

- Initial release to GitHub.

# General information

GitDiffWrapper is a helper for making GUI diff tools work well with GIT. It is
a console-mode .NET 4.8 program. All configuration options are provided via
command-line parameters (no configuration files or registry settings).

When GitDiffWrapper runs, it does the following:

1. Parses the command-line parameters. Some of the parameters will be specific
   to GitDiffWrapper, while others will need to be forwarded to `git diff`.
2. Spawn a background (detached) process that will clean up the temporary
   files when both GitDiffWrapper and the GUI diff tool have exited (i.e. so
   that cleanup runs even if you use Ctrl-C to cancel GitDiffWrapper).
3. Run `git diff --raw <args...>` to get information about the files to be
   compared. The `<args...>` passed to `git diff` are based on the parameters
   passed to GitDiffWrapper.
4. Prepares temporary folders with the files to be compared. This means
   running `git cat-file` to extract file content from the git repo. It also
   might require copying files from your git working directory.
5. Launches your GUI diff tool with appropriate parameters. By default,
   GitDiffWrapper will launch windiff.exe, but this can be changed via the
   `--tool=MyTool.exe` parameter. By default, GitDiffWrapper passes arguments
   `-i listFile` to the GUI diff tool (which is perfect for windiff since it
   works best with a file list), but for other tools you can change this by
   passing something like `--args="$1 $2"` ($1 will be replaced with the name
   of the left-side temporary folder, and $2 will be replaced with the name of
   the right-side temporary folder).
6. Exits (to unblock your command prompt).
7. After your GUI diff tool exits, the background process will clean up the
   temporary files and then exit.

You'll probably want to make one or more batch files that launch GitDiffWrapper
with parameters appropriate for your specific GUI diff tool. A typical
launch script might look like this:

`@GitDiffWrapper.exe --tool=windiff.exe %*`

Typically, your launch script will set GitDiffWrapper-specific options. It
will pass through the remaining options (typically `git diff` options) at the
end of the GitDiffWrapper command line (i.e. by ending the GitDiffWrapper
command line with `%*`).

# GitDiffWrapper options

Your launch script might want to configure GitDiffWrapper with one or more of
the following options:

* `-uno`: by default, GitDiffWrapper will scan for untracked files and include
  them in the diff. This can be slow on large repositories. To disable this
  scan, use `-uno`.
* `--tool="<DiffProgram>"` configures the GUI diff tool to launch. The
  default tool is `windiff.exe`. For example: `--tool="odd.exe"`
* `--args="<DiffProgramArgs>"` configures the parameters to pass to the GUI
  diff tool. The default is `-i $F`, which works well for windiff. The
  `--args` option will perform substitutions for `$1` (left folder), `$2`
  (right folder), `$F` (list filename), and `$$` (dollar sign). For example:
  `--args="$1 $2"`
* `--git="<GitProgram>"` configures the GIT tool to launch when querying the
  GIT repository. The default is `git.exe`. For example: `--git=git.cmd`
* `--temp="<TempDir>"` configures the directory where GitDiffWrapper will
  stage the diff for the GUI tool. Environment variables are expanded. The
  default is `%TEMP%`.

In addition to the options that your launch script might use, GitDiffWrapper
accepts the following options:

* `-u` re-enables the scan for untracked files (overrides `-uno` on a
  last-option-wins basis).
* `-h` shows details on the arguments accepted by GitDiffWrapper.
* `-q` disables all informational messages (only warnings and errors will be
  shown).
* `-v` enables verbose diagnostic messages.
* GitDiffWrapper accepts `...` as a shortcut for `--relative`, which performs
  the comparison relative to the current directory. In other words, assuming
  you're using the default `gd` wrapper, `gd ...` is similar to
  `windiff /lo ...`.

# git diff options

GitDiffWrapper runs `git diff --raw <args...>` to generate the list of files to
be compared. As such, effective use of GitDiffWrapper requires a basic
understanding of `git diff`.

See [git-diff](https://git-scm.com/docs/git-diff) for details.

* `gd` will compare the index (the staging area) with the working directory
  (similar to `windiff /lo`).
* `gd <tree>` will compare `<tree>` with the working directory. `<tree>` can be
  a commit, a branch, HEAD, etc. For example: `gd HEAD` compares the last
  commit against the working directory.
* `gd <tree1> <tree2>` will compare two trees. For example: `gd HEAD~1 HEAD`
  compares the next-to-last commit against the last commit.
* `gd <tree1>..<tree2>` will compare two trees (same as `gd <tree1> <tree2>`).
* `gd <tree1>...<tree2>` will show the changes that `<tree2>` contains that are
  not present in `<tree1>`. Specifically, it finds the nearest common ancestor
  of the two trees and compares that with `<tree2>`. For example:
  `gd ParentBranch HEAD` shows everything that has happend on the current
  branch since it was last merged with ParentBranch.
* `gd --cached <tree>` will compare `<tree>` with the index (the staging area).
  If `<tree>` is not given, it defaults to HEAD.

GitDiffWrapper does not expose all capabilities of `git diff`. It will ignore
(and warn about) any option it doesn't understand. GitDiffWrapper does accept
the following `git diff` options and will forward them to `git diff`:

*  `-B[...]`   (break rewrites)
*  `-C[...]`   (find copies)
*  `-G[...]`   (look for changes affecting specified regex)
*  `-l[...]`   (limit rename/copy processing time)
*  `-M[...]`   (find renames)
*  `-R`        (swap left and right sides)
*  `-S[...]`   (look for changes affecting specified string)
*  `--cached`  (compares with index instead of working directory)
*  `--staged`  (same as --cached)
*  `--break-rewrites[=...]`
*  `--diff-filter[=...]`
*  `--find-copies[=...]`
*  `--find-copies-harder[=...]`
*  `--find-renames[=...]`
*  `--no-renames`
*  `--relative`

Note that if you pass the `...` parameter to GitDiffWrapper, GitDiffWrapper
will add `--relative` to the `git diff` command line.
