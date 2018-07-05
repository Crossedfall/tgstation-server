﻿using System;

namespace Tgstation.Server.Host.Components.Watchdog
{
	/// <summary>
	/// Represents the result of trying to start a DD process
	/// </summary>
	public sealed class LaunchResult
	{
		/// <summary>
		/// The time it took for <see cref="System.Diagnostics.Process.WaitForInputIdle"/> to return
		/// </summary>
		public TimeSpan StartupTime { get; set; }

		/// <summary>
		/// The <see cref="System.Diagnostics.Process.ExitCode"/> if it exited
		/// </summary>
		public int? ExitCode { get; set; }

		/// <summary>
		/// The peak virtual memory usage in bytes
		/// </summary>
		public long PeakMemory { get; set; }
	}
}