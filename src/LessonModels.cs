using System;
using System.Collections.Generic;
using System.Linq;

namespace DeltaVis
{
	public class DeltaPair
	{
		public int Delta { get; set; }
		public int AdaptedDelta { get; set; }
	}

	public class LessonData
	{
		public int LessonId { get; set; }
		public List<DeltaPair> DeltaPairs { get; set; } = [];

		public LessonStatistics CalculateStatistics()
		{
			return new LessonStatistics {
				LessonId = LessonId,
				EvaluatedMoveCount = DeltaPairs.Count,
				MinDelta = DeltaPairs.Any() ? DeltaPairs.Min(p => p.Delta) : 0,
				AvgDelta = DeltaPairs.Any() ? (int)Math.Round(DeltaPairs.Average(p => p.Delta)) : 0,
				MaxDelta = DeltaPairs.Any() ? DeltaPairs.Max(p => p.Delta) : 0,
				MinAdapted = DeltaPairs.Any() ? DeltaPairs.Min(p => p.AdaptedDelta) : 0,
				AvgAdapted = DeltaPairs.Any() ? (int)Math.Round(DeltaPairs.Average(p => p.AdaptedDelta)) : 0,
				MaxAdapted = DeltaPairs.Any() ? DeltaPairs.Max(p => p.AdaptedDelta) : 0
			};
		}
	}

	public class LessonStatistics
	{
		public int RowNum { get; set; }
		public int LessonId { get; set; }
		public int EvaluatedMoveCount { get; set; }
		public int ExecutedMoveCount { get; set; }
		public int DistinctTimeslots { get; set; }
		public int EdgeCount { get; set; }
		public int MinDelta { get; set; }
		public int AvgDelta { get; set; }
		public int MaxDelta { get; set; }
		public int MinAdapted { get; set; }
		public int AvgAdapted { get; set; }
		public int MaxAdapted { get; set; }

		// Resolved from organisation data
		public string OrgLecturers { get; set; }
		public string OrgModule { get; set; }
		public string OrgTeachingGroup { get; set; }
		public string OrgSubGroups { get; set; }
		public string OrgClassGroups { get; set; }
	}
}
