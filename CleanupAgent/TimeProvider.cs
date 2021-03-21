using System;

namespace CleanupAgent
{
	public class TimeProvider : ITimeProvider
	{
		public DateTime NowUtc() => DateTime.UtcNow;
	}
}