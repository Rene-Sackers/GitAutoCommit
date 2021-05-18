using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;

namespace GitAutoCommit
{
	public static class Extensions
	{
		private const int MaxCommitMessageLength = 72;
		
		public static string ToReadableString(this FileStatus fileStatus)
		{
			switch (fileStatus)
			{
				case FileStatus.NewInIndex:
				case FileStatus.NewInWorkdir:
					return "created";
				case FileStatus.ModifiedInIndex:
				case FileStatus.ModifiedInWorkdir:
					return "modified";
				case FileStatus.DeletedFromIndex:
				case FileStatus.DeletedFromWorkdir:
					return "deleted";
				case FileStatus.RenamedInIndex:
				case FileStatus.RenamedInWorkdir:
					return "renamed";
				default:
					return fileStatus.ToString();
			}
		}

		public static string ToReadableString(this StatusEntry status)
		{
			if ((status.State & FileStatus.RenamedInIndex) == FileStatus.RenamedInIndex ||
				(status.State & FileStatus.RenamedInWorkdir) == FileStatus.RenamedInWorkdir)
			{
				var oldFilePath = ((status.State & FileStatus.RenamedInIndex) != 0)
					? status.HeadToIndexRenameDetails.OldFilePath
					: status.IndexToWorkDirRenameDetails.OldFilePath;

				return string.Format(CultureInfo.InvariantCulture, "{0}: {1} -> {2}", status.State.ToReadableString(), oldFilePath, status.FilePath);
			}

			return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", status.State.ToReadableString(), status.FilePath);
		}

		public static string ToReadableString(this RepositoryStatus status)
		{
			return string.Format(CultureInfo.InvariantCulture,
				"+{0} ~{1} -{2} | +{3} ~{4} -{5} | i{6}",
				status.Added.Count(),
				status.Staged.Count(),
				status.Removed.Count(),
				status.Untracked.Count(),
				status.Modified.Count(),
				status.Missing.Count(),
				status.Ignored.Count());
		}

		public static string ToCommitMessage(this RepositoryStatus status)
		{
			var stringBuilder = new StringBuilder();
			var lastState = (string) null;
			foreach (var state in status.OrderBy(s => s.State))
			{
				var thisState = state.State.ToReadableString();
				if (thisState == lastState)
				{
					stringBuilder.Append($", {Path.GetFileName(state.FilePath)}");
					continue;
				}
				
				stringBuilder.Append($"{(lastState == null ? string.Empty : ", ")}{thisState}: {Path.GetFileName(state.FilePath)}");
				lastState = thisState;
			}

			var commitMessage = stringBuilder.ToString();
			if (commitMessage.Length <= MaxCommitMessageLength)
				return commitMessage;
			
			const string newlineInsert = "...\n\n";
			commitMessage = commitMessage[..(MaxCommitMessageLength - 3)] + newlineInsert + commitMessage[(MaxCommitMessageLength - 3)..];

			return commitMessage;
		}

		public static bool IsGitFileChange(this FileSystemEventArgs e)
			=> e.FullPath.Contains($"{Path.DirectorySeparatorChar}.git");
	}
}