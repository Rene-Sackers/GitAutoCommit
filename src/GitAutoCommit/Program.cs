using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GitAutoCommit
{
	public class Program
	{
		private static readonly TimeSpan DebounceTimeout = TimeSpan.FromSeconds(5);

		private static CancellationTokenSource _debounceCancellationToken;
		private static string _gitAuthorName;
		private static string _gitAuthorEmail;

		public static async Task<int> Main(string[] args)
		{
			var rootCommand = new RootCommand
			{
				new Option<string>("--author-name", "The Git commit author name"),
				new Option<string>("--author-email", "The Git commit author email"),
				new Option<DirectoryInfo>("--path", "The path to the directory to monitor and commit in")
			};

			rootCommand.Description = "Git Auto Commit";
			rootCommand.Handler = CommandHandler.Create<string, string, DirectoryInfo>(async (authorName, authorEmail, path) =>
			{
				if (string.IsNullOrWhiteSpace(authorName))
					authorName = QueryForParameter("Git author name: ");
				if (string.IsNullOrWhiteSpace(authorEmail))
					authorEmail = QueryForParameter("Git author email: ");
				if (string.IsNullOrWhiteSpace(path?.Name))
					path = new(QueryForParameter("Path to watch: "));

				if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(authorEmail) || string.IsNullOrWhiteSpace(path?.Name))
				{
					Console.WriteLine("Missing parameters");
					return 1;
				}

				_gitAuthorName = authorName;
				_gitAuthorEmail = authorEmail;

				await StartApplication(path.FullName);
				return 0;
			});

			return await rootCommand.InvokeAsync(args);
		}

		private static string QueryForParameter(string message)
		{
			while (true)
			{
				Console.Write(message);
				var response = Console.ReadLine();

				if (!string.IsNullOrWhiteSpace(response))
					return response;

				Console.WriteLine("Please specify a valid value.");
			}
		}

		private static async Task StartApplication(string path)
		{
			Console.Title = "Git auto commit: " + path;

			Repository repo;
			try
			{
				repo = new(path);
			}
			catch (RepositoryNotFoundException)
			{
				Console.WriteLine("Specified directory " + path + " is not a Git repo. Would you like to turn it into one?");
				Console.Write("Y/N: ");
				var answer = Console.ReadKey();
				if (answer.KeyChar.ToString().ToLower() != "y")
					return;

				Repository.Init(path);
				Console.WriteLine("\nInitialized repository");
				repo = new(path);
			}
			
			var watcher = new FileSystemWatcher(path);
			watcher.Changed += (_, e) => FileSystemEventHandler(e, repo);
			watcher.Created += (_, e) => FileSystemEventHandler(e, repo);
			watcher.Deleted += (_, e) => FileSystemEventHandler(e, repo);
			watcher.Renamed += (_, _) => CommitChanges(repo);
			watcher.EnableRaisingEvents = true;

			var stopCancellation = new CancellationTokenSource();
			Console.CancelKeyPress += (_, _) => stopCancellation.Cancel();
			
			CommitChanges(repo);

			Console.WriteLine("Running for directory: " + path);
			
			await Task.Delay(-1, stopCancellation.Token);
		}

		private static void FileSystemEventHandler(FileSystemEventArgs e, Repository repo)
		{
			if (!e.IsGitFileChange())
				CommitChanges(repo);
		}

		private static async void CommitChanges(IRepository repo)
		{
			Console.WriteLine("File change detected");
			
			if (_debounceCancellationToken is {IsCancellationRequested: false})
				_debounceCancellationToken.Cancel(false);

			var newToken = new CancellationTokenSource();
			_debounceCancellationToken = newToken;
			try
			{
				await Task.Delay(DebounceTimeout, newToken.Token);
			
				if (newToken.IsCancellationRequested)
				{
					throw new TaskCanceledException();
				}
			}
			catch (TaskCanceledException)
			{
				return;
			}
			
			try
			{
				var status = repo.RetrieveStatus(new StatusOptions());
				if (!status.Any())
				{
					Console.WriteLine("No changes");
					return;
				}
				
				Console.WriteLine(status.ToReadableString());
				status.ToList().ForEach(s => Console.WriteLine(s.ToReadableString()));
				
				Commands.Stage(repo, "*");
				var signature = new Signature(_gitAuthorName, _gitAuthorEmail, DateTimeOffset.Now);
				repo.Commit(status.ToCommitMessage(), signature, signature);
				
				Console.WriteLine("Changes committed");
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}
		}
	}
}