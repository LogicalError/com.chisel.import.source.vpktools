using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// How long each step of an import batch took, summed over everything it imported, said once at the batch's end as
	/// "&lt;label&gt; (steps)". A step inside another stops the outer one's clock until it ends, so each step's time is its
	/// own and the steps add up to the batch. A batch begun inside another adds its steps to that one and says nothing of
	/// its own: the materials of a map's models are steps of the model batch. Outside a batch a step times nothing.
	/// </summary>
	internal static class ImportSteps
	{
		internal sealed class Run
		{
			internal readonly string Label;
			internal readonly Stopwatch Total = Stopwatch.StartNew();
			internal readonly List<(string name, Stopwatch watch)> Steps = new();
			internal readonly Stack<Stopwatch> Active = new();

			internal Run(string label) => Label = label;

			internal Stopwatch Watch(string name)
			{
				foreach (var (stepName, watch) in Steps)
				{
					if (stepName == name)
						return watch;
				}
				var added = new Stopwatch();
				Steps.Add((name, added));
				return added;
			}

			internal string Report()
			{
				var parts = new List<string>(Steps.Count);
				foreach (var (name, watch) in Steps)
					parts.Add($"{name} {watch.Elapsed.TotalSeconds:0.0}s");
				return string.Join(", ", parts);
			}
		}

		static Run s_Run;

		/// <summary>A batch begun by <see cref="Begin"/>; ending one begun inside another does nothing.</summary>
		internal readonly struct Batch
		{
			readonly Run run;

			internal Batch(Run run) => this.run = run;

			/// <summary>Ends the batch, and says its steps when <paramref name="say"/> is set, after what <paramref name="detail"/>
			/// says of it.</summary>
			internal void End(string detail, bool say)
			{
				if (run == null)
					return;
				s_Run = null;
				if (say)
					UnityEngine.Debug.Log($"{run.Label} (steps): {detail}, {run.Total.Elapsed.TotalSeconds:0.0}s - {run.Report()}");
			}
		}

		internal static Batch Begin(string label)
		{
			if (s_Run != null)
				return default;
			s_Run = new Run(label);
			return new Batch(s_Run);
		}

		/// <summary>Times what it encloses into the named step of the batch running, if one is.</summary>
		internal static Scope Step(string name) => new Scope(s_Run, name);

		internal readonly struct Scope : IDisposable
		{
			readonly Run run;
			readonly Stopwatch watch;

			internal Scope(Run run, string name)
			{
				this.run = run;
				watch = run?.Watch(name);
				if (run == null)
					return;
				// The step it is inside stops until it ends
				if (run.Active.Count > 0)
					run.Active.Peek().Stop();
				run.Active.Push(watch);
				watch.Start();
			}

			public void Dispose()
			{
				if (run == null)
					return;
				watch.Stop();
				if (run.Active.Count > 0 && run.Active.Peek() == watch)
					run.Active.Pop();
				if (run.Active.Count > 0)
					run.Active.Peek().Start();
			}
		}
	}
}
