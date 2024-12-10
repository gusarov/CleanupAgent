using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanupAgent
{
	internal interface ICleaner
	{
	}

	internal class CleanupSettings
	{
		public bool IsDryRun { get; set; }
	}

	internal class FileCleaner : ICleaner
	{

	}

	internal class DockerCleaner : ICleaner
	{

	}

	internal class CleanerFactory
	{
		public IEnumerable<ICleaner> CreateCleaners()
		{
			yield break;
		}
	}

}
