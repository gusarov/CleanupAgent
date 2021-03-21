namespace CleanupAgent
{
	public interface IResultLog
	{
		public void Message(string message);
		public void Error(string message);
	}
}