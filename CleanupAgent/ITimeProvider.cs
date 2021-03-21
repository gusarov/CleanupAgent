using System;

namespace CleanupAgent
{
	public interface ITimeProvider
	{
		public DateTime NowUtc();
	}
}