using System;

namespace CleanupAgent
{
	public class ConsoleResultLog : IResultLog
	{
		public bool AtLeastOneError { get; private set; }

		public void Message(string message)
		{
			var c = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine(message);
			Console.ForegroundColor = c;
		}

		public void Error(string message)
		{
			AtLeastOneError = true;
			var c = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine("Error: " + message);
			Console.ForegroundColor = c;
		}
	}
}