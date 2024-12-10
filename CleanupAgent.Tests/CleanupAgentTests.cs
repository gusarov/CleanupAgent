using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CleanupAgent.Tests
{
	class TestTimeProvider : ITimeProvider
	{
		public DateTime NowUtc()
		{
			return Clock;
		}

		public DateTime Clock { get; set; } = DateTime.UtcNow; // now by default, because tests are creating real files and it produces current time stamps
	}

	class TestLogProvider : IResultLog
	{
		public void Message(string message)
		{
			
		}

		public void Error(string message)
		{

		}
	}

	[TestClass]
	public class CleanupAgentTests
	{
		private readonly TestTimeProvider _time = new();
		private readonly TestLogProvider _log = new();
		private CleanupTools _sut;

		[TestInitialize]
		public void Setup()
		{
			_sut = new CleanupTools(_time, _log)
			{
				DryRun = false,
			};

			if (Directory.Exists("Data"))
			{
				foreach (var file in Directory.GetFiles("Data", "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
					File.Delete(file);
				}
				foreach (var dir in Directory.GetDirectories("Data", "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(dir, FileAttributes.Normal);
					Directory.Delete(dir);
				}
				Directory.Delete("Data", true);
			}

			Directory.CreateDirectory("Data/A");
			File.WriteAllText("Data/desktop.ini", "1");
			File.SetAttributes("Data/desktop.ini", FileAttributes.Normal | FileAttributes.Hidden | FileAttributes.System);

			File.WriteAllText("Data/A/temp1.txt", "1");
			File.WriteAllText("Data/A/desktop.ini", "1");
			File.SetAttributes("Data/A/desktop.ini", FileAttributes.Normal | FileAttributes.Hidden | FileAttributes.System);

			File.WriteAllText("Data/A/temp1_sys.txt", "1");
			File.SetAttributes("Data/A/temp1_sys.txt", FileAttributes.Normal | FileAttributes.System);

			File.WriteAllText("Data/A/temp1_hid.txt", "1");
			File.SetAttributes("Data/A/temp1_hid.txt", FileAttributes.Normal | FileAttributes.Hidden);

			File.WriteAllText("Data/A/temp1_ro.txt", "1");
			File.SetAttributes("Data/A/temp1_ro.txt", FileAttributes.Normal | FileAttributes.ReadOnly);

			File.WriteAllText("Data/A/temp1_sys_hid.txt", "1");
			File.SetAttributes("Data/A/temp1_sys_hid.txt", FileAttributes.Normal | FileAttributes.System | FileAttributes.Hidden);

			Directory.CreateDirectory("Data/B");
			File.SetAttributes("Data/B", FileAttributes.Normal | FileAttributes.System);
			File.WriteAllText("Data/B/temp2.txt", "2");

			Directory.CreateDirectory("Data/C");
			File.SetAttributes("Data/C", FileAttributes.Normal | FileAttributes.Hidden);
			File.WriteAllText("Data/C/temp2.txt", "2");

			Directory.CreateDirectory("Data/D");
			File.SetAttributes("Data/D", FileAttributes.Normal | FileAttributes.ReadOnly);
			File.WriteAllText("Data/D/temp2.txt", "2");

			Directory.CreateDirectory("Data/E");
			File.SetAttributes("Data/E", FileAttributes.Normal | FileAttributes.System | FileAttributes.Hidden);
			File.WriteAllText("Data/E/temp2.txt", "2");
		}

		private long _total = 16;

		private ContextSettings _contextAll = new()
		{

		};

		private ContextSettings _context30 = new()
		{
			TimeToKeep = TimeSpan.FromDays(30),
		};

		[TestMethod]
		public void Should_00_know_data_content()
		{
			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_10_DeleteContent_with_system_and_hidden_files()
		{
			_sut.DeleteOldContent("Data", default);
			Assert.AreEqual(1, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
			Assert.IsTrue(File.Exists("Data/desktop.ini"), "special file should stay");
		}

		[TestMethod]
		public void Should_10_not_DeleteContent_when_dry_run()
		{
			_sut.DryRun = true;
			_sut.DeleteOldContent("Data", default);
			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_10_DeleteContent_with_locked_files()
		{
			using (File.OpenRead("Data/A/temp1.txt"))
			{
				_sut.DeleteOldContent("Data", default);
				Assert.AreEqual(3, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
			}
		}

		[TestMethod]
		public void Should_20_DeleteOldContent()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc =
			file.LastWriteTimeUtc =
			file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total - 1, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_20_DeleteOldContent_if_all_2_dates_are_old_1()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc = DateTime.UtcNow.AddDays(-40);
			// file.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-40);
			file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_20_DeleteOldContent_if_all_2_dates_are_old_2()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc = DateTime.UtcNow.AddDays(-40);
			file.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-40);
			// file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_20_DeleteOldContent_if_all_2_dates_are_old_3()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc = DateTime.UtcNow;
			file.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-40);
			file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total - 1, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_20_DeleteOldContent_regardless_off_access_date()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc = DateTime.UtcNow;
			file.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-40);
			file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total - 1, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_20_DeleteOldContent_with_old_folder()
		{
			var file = new FileInfo("Data/B/temp2.txt");
			file.LastAccessTimeUtc =
				file.LastWriteTimeUtc =
					file.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			var dir = new DirectoryInfo("Data/B");
			dir.LastAccessTimeUtc =
				dir.LastWriteTimeUtc =
					dir.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total - 2, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}


		[TestMethod]
		public void Should_20_DeleteOldContent_old_folder()
		{
			var dir = new DirectoryInfo("Data/B");
			dir.LastAccessTimeUtc =
			dir.LastWriteTimeUtc =
			dir.CreationTimeUtc = DateTime.UtcNow.AddDays(-40);

			_sut.DeleteOldContent("Data", _context30);
			Assert.AreEqual(_total, Directory.GetFileSystemEntries("Data", "*", SearchOption.AllDirectories).Length);
		}

		[TestMethod]
		public void Should_shrink_database()
		{
			Assert.Inconclusive();
		}

		[TestMethod]
		public void Should_shrink_docker()
		{
			Assert.Inconclusive();
		}
	}
}
