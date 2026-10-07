using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace DeltaVis
{
	public partial class Form1
	{
		private void LoadAndDisplayData(string filePath)
		{
			try
			{
				var lessons = FileParser.ParseDeltaFile(filePath);
				var stats = lessons.Select(l => l.CalculateStatistics()).OrderBy(s => s.LessonId).ToList();

				for (var i = 0; i < stats.Count; i++)
					stats[i].RowNum = i + 1;

				var bindingList = new BindingList<LessonStatistics>(stats);
				MergePositionsData(bindingList);
				MergeGraphData(bindingList);
				MergeOrgData(bindingList);
				bindingSource = new BindingSource { DataSource = bindingList };
				dataGridView.DataSource = bindingSource;
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void LoadAndDisplayFromPositions()
		{
			try
			{
				if (positionsData == null || positionsData.Count == 0)
				{
					MessageBox.Show("No position data loaded.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				var stats = positionsData
					.OrderBy(p => p.Key)
					.Select((p, index) => new LessonStatistics
					{
						RowNum = index + 1,
						LessonId = p.Key,
						ExecutedMoveCount = p.Value.ExecutedMoves,
						DistinctTimeslots = p.Value.Timeslots.Count,
						EvaluatedMoveCount = 0,
						MinDelta = 0,
						AvgDelta = 0,
						MaxDelta = 0,
						MinAdapted = 0,
						AvgAdapted = 0,
						MaxAdapted = 0
					})
					.ToList();

				var bindingList = new BindingList<LessonStatistics>(stats);
				MergeGraphData(bindingList);
				MergeOrgData(bindingList);
				bindingSource = new BindingSource { DataSource = bindingList };
				dataGridView.DataSource = bindingSource;
				hasDeltaData = false;
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error displaying positions data: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void MergePositionsData(BindingList<LessonStatistics> bindingList)
		{
			if (positionsData == null || positionsData.Count == 0)
				return;

			foreach (var stat in bindingList)
			{
				if (positionsData.TryGetValue(stat.LessonId, out var positionInfo))
				{
					stat.ExecutedMoveCount = positionInfo.ExecutedMoves;
					stat.DistinctTimeslots = positionInfo.Timeslots.Count;
				}
				else
				{
					stat.ExecutedMoveCount = 0;
					stat.DistinctTimeslots = 0;
				}
			}
		}

		private void MergeGraphData(BindingList<LessonStatistics> bindingList)
		{
			if (graphData == null || graphData.Count == 0)
				return;

			foreach (var stat in bindingList)
			{
				stat.EdgeCount = graphData.TryGetValue(stat.LessonId, out var edges) ? edges.Count : 0;
			}
		}

		private void MergeOrgData(BindingList<LessonStatistics> bindingList)
		{
			if (orgData == null || orgData.Count == 0)
				return;

			foreach (var stat in bindingList)
			{
				if (orgData.TryGetValue(stat.LessonId, out var info))
				{
					stat.OrgLecturers     = info.Lecturers;
					stat.OrgModule        = info.Module;
					stat.OrgTeachingGroup = info.TeachingGroup;
					stat.OrgSubGroups     = info.SubGroups;
					stat.OrgClassGroups   = info.ClassGroups;
				}
			}
		}
	}
}
