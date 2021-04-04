using System;
using System.Diagnostics;
using System.IO;

namespace CleanupAgent
{
	public class Context
	{
		public TimeSpan TimeToKeep { get; set; }
		public long TotalLogicalBytes { get; set; }
		public long TotalItems { get; set; }

		internal int Level { get; set; }
	}

	public class CleanupTools
	{
		private readonly ITimeProvider _timeProvider;
		private readonly IResultLog _log;

		public bool? DryRun { get; set; }

		public CleanupTools(ITimeProvider timeProvider, IResultLog log)
		{
			_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
			_log = log ?? throw new ArgumentNullException(nameof(log));
		}

		/// <summary>
		/// :clean
		/// This method deletes all files and folders from specified location, but never deletes folder itself, this way ACLS are preserved
		/// Useful for Temporary ASP.NET Files that are critical to ACL
		/// </summary>
		public void DeleteContent(string folder)
		{
			DeleteOldContent(folder, new Context());
		}

		/// <summary>
		/// :old
		/// This method deletes all old files and folders from specified location
		/// </summary>
		public void DeleteOldContent(string folder, Context context)
		{
			var cd = new DirectoryInfo(folder);
			DeleteOldContent(cd, context);
		}

		public void DeleteOldContent(DirectoryInfo folder, Context context)
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}
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

		private void JudgeAndDelete(Context context, FileSystemInfo fsInfo, DateTime now)
		{
			var delete = false;
			if (context.TimeToKeep != default)
			{
				var age = TouchedAgo(fsInfo, now);
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

					context.TotalItems++;
					context.TotalLogicalBytes += logicalSize;
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

		/// <summary>
		/// :user
		/// This method performs cleanup from user profile, so can be reused per user
		/// </summary>
		public void CleanupUserProfile(string profilePath, Context context)
		{
			DeleteOldContent(Path.Combine(profilePath, "AppData/Local/Temp"), context);
			DeleteOldContent(Path.Combine(profilePath, "AppData/Local/Microsoft/Windows/IECompatCache"), context);
			DeleteOldContent(Path.Combine(profilePath, "AppData/Local/Microsoft/Windows/IECompatUaCache"), context);
			DeleteOldContent(Path.Combine(profilePath, "AppData/Local/Microsoft/Windows/IEDownloadHistory"), context);
			DeleteOldContent(Path.Combine(profilePath, "AppData/Local/Microsoft/Windows/INetCache"), context);
			DeleteOldContent(Path.Combine(profilePath, "Downloads"), context);

			// Optimize docker vhd
			var path = Path.Combine(profilePath, @"AppData\Local\Docker\wsl\data\ext4.vhdx");
			if (File.Exists(path))
			{
				var orig = new FileInfo(path).Length;
				Run("wsl", $"--shutdown");
				Run("net", $"start vmms");
				Run("powershell", $"-NonInteractive -Command \"& {{Optimize-VHD -Path {path} -Mode Full}}\"");
				var newSize = new FileInfo(path).Length;
				context.TotalLogicalBytes += orig - newSize;
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