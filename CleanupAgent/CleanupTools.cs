using System;
using System.Diagnostics;
using System.IO;

namespace CleanupAgent
{
	/// <summary>
	/// Mostly statistics or scan-wide data
	/// </summary>
	public class Statistics
	{
		public long TotalLogicalBytes { get; set; }
		public long TotalItems { get; set; }
	}

	/// <summary>
	/// Mostly options to let it override as environment (copy on call)
	/// </summary>
	public struct ContextSettings
	{
		public TimeSpan TimeToKeep { get; set; }
		internal int Level { get; set; }
	}

	public class CleanupTools
	{
		private readonly ITimeProvider _timeProvider;
		private readonly IResultLog _log;

		public bool? DryRun { get; set; }

		Statistics _statistics;
		public Statistics Statistics => _statistics;

		public CleanupTools(ITimeProvider timeProvider, IResultLog log)
		{
			_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
			_log = log ?? throw new ArgumentNullException(nameof(log));
			_statistics= new Statistics();
		}

		/*
		/// <summary>
		/// :clean
		/// This method deletes all files and folders from specified location, but never deletes folder itself, this way ACLS are preserved
		/// Useful for Temporary ASP.NET Files that are critical to ACL
		/// </summary>
		public void DeleteContent(string folder)
		{
			DeleteOldContent(folder, new Context());
		}
		*/

		/// <summary>
		/// :old
		/// This method deletes all old files and folders from specified location
		/// </summary>
		public void DeleteOldContent(string folder, ContextSettings context)
		{
			var cd = new DirectoryInfo(folder);
			DeleteOldContent(cd, context);
		}

		public void DeleteOldContent(DirectoryInfo folder, ContextSettings context)
		{
			if (context.TimeToKeep.TotalDays < 0)
			{
				throw new ArgumentException("Time to keep", nameof(context));
			}

			if (!folder.Exists)
			{
				return;
			}
			var now = _timeProvider.NowUtc();
			try
			{
				/*
				if ((folder.Attributes & FileAttributes.ReparsePoint) > 0)
				{
					var target = PInvoke.GetTarget(folder.FullName);
					Console.WriteLine(target);
				}
				*/
				var dirs = folder.GetDirectories();
				foreach (var dir in dirs)
				{
					try
					{
						context.Level++;
						DeleteOldContent(dir, context);
					}
					finally
					{
						context.Level--;
					}
					JudgeAndDelete(context, dir, now);
				}
				var files = folder.GetFiles();
				foreach (var file in files)
				{
					if (context.Level == 0)
					{
						if (file.Name.ToLowerInvariant() == "desktop.ini")
						{
							continue;
						}
					}
					JudgeAndDelete(context, file, now);
				}
			}
			catch (Exception ex)
			{
				_log.Error($"{folder.FullName}: {ex.Message}");
			}
		}

		private void JudgeAndDelete(ContextSettings context, FileSystemInfo fsInfo, DateTime now)
		{
			var delete = false;
			if (context.TimeToKeep != default)
			{
				var age = WrittenAgo(fsInfo, now);
				// var age = TouchedAgo(fsInfo, now);
				if (age > context.TimeToKeep)
				{
					delete = true;
				}
			}
			else
			{
				delete = true;
			}

			if (delete)
			{
				try
				{
					var logicalSize = 0L;
					if (fsInfo is FileInfo file)
					{
						logicalSize = file.Length;
					}
					if (DryRun.GetValueOrDefault(true))
					{
						_log.Message($"DryRun: {fsInfo.FullName}");
					}
					else
					{
						_log.Message($"Deleting: {fsInfo.FullName}");
						var attrs = fsInfo.Attributes;
						if ((attrs & FileAttributes.ReadOnly) > 0
						    || (attrs & FileAttributes.System) > 0
						)
						{
							fsInfo.Attributes = FileAttributes.Normal;
						}

						fsInfo.Delete();
					}

					_statistics.TotalItems++;
					_statistics.TotalLogicalBytes += logicalSize;
				}
				catch (IOException ex)
				{
					_log.Error($"{fsInfo.FullName}: {ex.Message}");
				}
			}
		}

		TimeSpan TouchedAgo(FileSystemInfo fs, DateTime from)
		{
			var age = from - fs.LastAccessTimeUtc;
			var wri = from - fs.LastWriteTimeUtc;
			if (wri < age)
			{
				age = wri;
			}
			var cre = from - fs.CreationTimeUtc;
			if (cre < age)
			{
				age = cre;
			}
			return age;
		}

		TimeSpan WrittenAgo(FileSystemInfo fs, DateTime now)
		{
			var wri = now - fs.LastWriteTimeUtc;
			var cre = now - fs.CreationTimeUtc;
			return cre < wri ? cre : wri;
		}

		/// <summary>
		/// :user
		/// This method performs cleanup from user profile, so can be reused per user
		/// </summary>
		public void CleanupUserProfile(string profilePath, ContextSettings context)
		{
			var weekContext = context;
			weekContext.TimeToKeep = TimeSpan.FromDays(7);

			var monthContext = context;
			monthContext.TimeToKeep = TimeSpan.FromDays(30);

			DeleteOldContent(Path.Combine(profilePath, "AppData\\Local\\Temp"), weekContext);
			DeleteOldContent(Path.Combine(profilePath, "AppData\\Local\\Microsoft\\Windows\\IECompatCache"), weekContext);
			DeleteOldContent(Path.Combine(profilePath, "AppData\\Local\\Microsoft\\Windows\\IECompatUaCache"), weekContext);
			DeleteOldContent(Path.Combine(profilePath, "AppData\\Local\\Microsoft\\Windows\\IEDownloadHistory"), weekContext);
			DeleteOldContent(Path.Combine(profilePath, "AppData\\Local\\Microsoft\\Windows\\INetCache"), weekContext);
			DeleteOldContent(Path.Combine(profilePath, "Downloads"), monthContext);

			DeleteFolderMonthly(Path.Combine(profilePath, "AppData\\Roaming\\npm-cache"));
			DeleteFolderMonthly(Path.Combine(profilePath, "AppData\\Local\\NuGet"));
			DeleteFolderMonthly(Path.Combine(profilePath, ".nuget\\packages"));

			// Optimize docker vhd
			var path = Path.Combine(profilePath, @"AppData\Local\Docker\wsl\data\ext4.vhdx");
			if (File.Exists(path) && DryRun == false)
			{
				var orig = new FileInfo(path).Length;
				Run("wsl", $"--shutdown");
				Run("net", $"start vmms");
				Run("powershell", $"-NonInteractive -Command \"& {{Optimize-VHD -Path {path} -Mode Full}}\"");
				var newSize = new FileInfo(path).Length;
				_statistics.TotalLogicalBytes += orig - newSize;
			}
		}

		/// <summary>
		/// e.g. NuGet cache, etc
		/// </summary>
		/// <param name="path"></param>
		/// <param name="context"></param>
		private void DeleteFolderMonthly(string path)
		{
			var fi = new DirectoryInfo(path);
			if (fi.Exists && (DateTime.UtcNow - fi.CreationTimeUtc).TotalDays > 30)
			{
				try
				{
					fi.Delete(true);
				}
				catch (IOException ex)
				{
					_log.Error($"{fi.FullName}: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// :docker
		/// This method performs cleanup from docker
		/// </summary>
		public void CleanupDocker()
		{
			Run("docker", "system prune -fa --filter until=168h");
			Run("docker", "system prune -fa --volumes");
		}

		internal void Run(string cmd, string args)
		{
			try
			{
				var p = Process.Start(cmd, args);
				p.WaitForExit();
				if (p.ExitCode != 0)
				{
					throw new Exception("Exit Code: " + p.ExitCode);
				}
			}
			catch (Exception ex)
			{
				_log.Error($"Process: {cmd}: " + ex.Message);
			}
		}
	}
}