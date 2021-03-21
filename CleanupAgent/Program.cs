using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CleanupAgent
{
	class Program
	{
		static void Help()
		{
			Console.WriteLine("Performs maintenance and cleanup of old or temporary files");
			Console.WriteLine();
			Console.WriteLine("CleanupAgent [/confirm] [/dryrun]");
		}

		static int Main(string[] args)
		{
			var errorLog = new ConsoleResultLog();
			var cleaner = new CleanupTools(new TimeProvider(), errorLog);

			var context = new Context
			{
				TimeToKeep = TimeSpan.FromDays(30),
			};

			// before stopping wsl for shrinking, let's docker system purge
			cleaner.CleanupDocker();

			for (int i = 0; i < args.Length; i++)
			{
				var arg = args[i];
				if (arg.StartsWith('-') || arg.StartsWith('/'))
				{
					arg = arg.Substring(1);
					if (arg.StartsWith('-'))
					{
						arg = arg.Substring(1);
					}
					switch (arg.ToLowerInvariant())
					{
						case "confirm":
						{
							if (cleaner.DryRun == null || cleaner.DryRun == false)
							{
								cleaner.DryRun = false;
							}
							else
							{
								Console.Error.WriteLine("DryRun can not be combined with Confirm");
							}
							break;
						}
						case "dryrun":
						{
							if (cleaner.DryRun == null || cleaner.DryRun == true)
							{
								cleaner.DryRun = true;
							}
							else
							{
								Console.Error.WriteLine("Confirm can not be combined with DryRun");
							}
							break;
						}
						default:
						{
							Console.Error.WriteLine("Bad option: " + args[i]);
							Help();
							return 1;
						}
					}
				}
			}

			// C:\TEMP
			foreach (var drive in DriveInfo.GetDrives())
			{
				var dir = Path.Combine(drive.RootDirectory.FullName, "TEMP");
				Console.WriteLine($"Cleanup {dir}...");
				try
				{
					cleaner.DeleteOldContent(dir, context);
				}
				catch (Exception ex)
				{
					errorLog.Error($"{drive.Name}: {ex.Message}");
				}
			}

			// C:\$Recycle.Bin\
			foreach (var drive in DriveInfo.GetDrives())
			{
				try
				{
					var dir = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
					Console.WriteLine($"Cleanup {dir}...");
					foreach (var userRecycle in Directory.GetDirectories(dir))
					{
						try
						{
							cleaner.DeleteOldContent(userRecycle, context);
						}
						catch (Exception ex)
						{
							errorLog.Error($"{userRecycle}: {ex.Message}");
						}
					}
				}
				catch (Exception ex)
				{
					errorLog.Error($"{drive.Name}: {ex.Message}");
				}
			}

			// System
			var msNet = Path.Combine(Environment.GetEnvironmentVariable("WinDir"), "Microsoft.NET");
			foreach (var dir in Directory.GetDirectories(Path.Combine(msNet, "Framework"), "v*"))
			{
				foreach (var tDir in Directory.GetDirectories(dir, "Temporary ASP.NET Files"))
				{
					Console.WriteLine(tDir);
					cleaner.DeleteContent(tDir);
				}
			}
			foreach (var dir in Directory.GetDirectories(Path.Combine(msNet, "Framework64"), "v*"))
			{
				foreach (var tDir in Directory.GetDirectories(dir, "Temporary ASP.NET Files"))
				{
					Console.WriteLine(tDir);
					cleaner.DeleteContent(tDir);
				}
			}

			// Profiles
			cleaner.CleanupUserProfile(Path.Combine(Environment.GetEnvironmentVariable("WinDir"), "System32\\config\\systemprofile"), context);
			cleaner.CleanupUserProfile(Path.Combine(Environment.GetEnvironmentVariable("WinDir"), "SysWOW64\\config\\systemprofile"), context);

			var users = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive"), "Users");
			foreach (var user in Directory.GetDirectories(users))
			{
				cleaner.CleanupUserProfile(user, context);
			}

			Console.WriteLine($"Total reclaimed: {context.TotalLogicalBytes:N0} bytes (logical)");
			Console.WriteLine($"Total deleted: {context.TotalItems} items");

			return errorLog.AtLeastOneError ? 1 : 0;
		}


	}
}
