using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeltaVis
{
	public partial class Form1
	{
		private void LoadFileButton_Click(object sender, EventArgs e)
		{
			if (openFileDialog.ShowDialog() == DialogResult.OK) {
				hasDeltaData = true;
				LoadAndDisplayData(openFileDialog.FileName);
			}
		}

		private void LoadPositionsButton_Click(object sender, EventArgs e)
		{
			if (openFileDialog.ShowDialog() == DialogResult.OK) {
				positionsData = FileParser.ParsePositionsFile(openFileDialog.FileName);

				if (hasDeltaData && bindingSource?.DataSource is BindingList<LessonStatistics> bindingList && bindingList.Count > 0) {
					MergePositionsData(bindingList);
					dataGridView.Refresh();
				}
				else {
					LoadAndDisplayFromPositions();
				}
			}
		}

		private void LoadGraphButton_Click(object sender, EventArgs e)
		{
			if (openFileDialog.ShowDialog() == DialogResult.OK) {
				graphData = FileParser.ParseGraphFile(openFileDialog.FileName);

				if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList && bindingList.Count > 0) {
					MergeGraphData(bindingList);
					dataGridView.Refresh();
				}

				graphPanel?.SetGraphData(graphData);
			}
		}

		private void LoadDataButton_Click(object sender, EventArgs e)
		{
			var dlg = new OpenFileDialog {
				Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
				Title = "Select Organisation Data File"
			};

			if (dlg.ShowDialog() != DialogResult.OK)
				return;

			try {
				orgData = OrgDataParser.Parse(dlg.FileName);

				if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList && bindingList.Count > 0) {
					MergeOrgData(bindingList);
					dataGridView.Refresh();
				}
				else if (orgData != null && orgData.Count > 0) {
					// If no table exists yet, create one from org data
					var stats = orgData
						.OrderBy(kvp => kvp.Key)
						.Select((kvp, index) => new LessonStatistics {
							RowNum = index + 1,
							LessonId = kvp.Key,
							OrgLecturers = kvp.Value.Lecturers,
							OrgModule = kvp.Value.Module,
							OrgTeachingGroup = kvp.Value.TeachingGroup,
							OrgSubGroups = kvp.Value.SubGroups,
							OrgClassGroups = kvp.Value.ClassGroups
						})
						.ToList();

					var bindingList2 = new BindingList<LessonStatistics>(stats);
					MergeGraphData(bindingList2);
					bindingSource = new BindingSource { DataSource = bindingList2 };
					dataGridView.DataSource = bindingSource;
				}

				graphPanel?.SetLessonTooltips(orgData);
			}
			catch (Exception ex) {
				MessageBox.Show($"Error loading data file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void DataGridView_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
		{
			var rowNum = (e.RowIndex + 1).ToString();
			var headerBounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, dataGridView.RowHeadersWidth, e.RowBounds.Height);
			TextRenderer.DrawText(e.Graphics, rowNum, dataGridView.Font, headerBounds, Color.Black,
				TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
		}

		private void DataGridView_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			if (bindingSource?.DataSource is not BindingList<LessonStatistics> bindingList)
				return;

			var columnName = dataGridView.Columns[e.ColumnIndex].DataPropertyName;
			if (string.IsNullOrEmpty(columnName))
				return;

			var list = bindingList.Cast<LessonStatistics>().ToList();

			var ascending = true;
			if (dataGridView.Tag is string lastSorted && lastSorted.StartsWith(columnName))
				ascending = !lastSorted.EndsWith("ASC");

			list = columnName switch {
				"LessonId" => ascending ? list.OrderBy(x => x.LessonId).ToList() : list.OrderByDescending(x => x.LessonId).ToList(),
				"OrgClassGroups" => ascending ? list.OrderBy(x => x.OrgClassGroups).ToList() : list.OrderByDescending(x => x.OrgClassGroups).ToList(),
				"OrgModule" => ascending ? list.OrderBy(x => x.OrgModule).ToList() : list.OrderByDescending(x => x.OrgModule).ToList(),
				"OrgLecturers" => ascending ? list.OrderBy(x => x.OrgLecturers).ToList() : list.OrderByDescending(x => x.OrgLecturers).ToList(),
				"OrgSubGroups" => ascending ? list.OrderBy(x => x.OrgSubGroups).ToList() : list.OrderByDescending(x => x.OrgSubGroups).ToList(),
				"OrgTeachingGroup" => ascending ? list.OrderBy(x => x.OrgTeachingGroup).ToList() : list.OrderByDescending(x => x.OrgTeachingGroup).ToList(),
				"EvaluatedMoveCount" => ascending ? list.OrderBy(x => x.EvaluatedMoveCount).ToList() : list.OrderByDescending(x => x.EvaluatedMoveCount).ToList(),
				"ExecutedMoveCount" => ascending ? list.OrderBy(x => x.ExecutedMoveCount).ToList() : list.OrderByDescending(x => x.ExecutedMoveCount).ToList(),
				"DistinctTimeslots" => ascending ? list.OrderBy(x => x.DistinctTimeslots).ToList() : list.OrderByDescending(x => x.DistinctTimeslots).ToList(),
				"EdgeCount" => ascending ? list.OrderBy(x => x.EdgeCount).ToList() : list.OrderByDescending(x => x.EdgeCount).ToList(),
				"MinDelta" => ascending ? list.OrderBy(x => x.MinDelta).ToList() : list.OrderByDescending(x => x.MinDelta).ToList(),
				"AvgDelta" => ascending ? list.OrderBy(x => x.AvgDelta).ToList() : list.OrderByDescending(x => x.AvgDelta).ToList(),
				"MaxDelta" => ascending ? list.OrderBy(x => x.MaxDelta).ToList() : list.OrderByDescending(x => x.MaxDelta).ToList(),
				"MinAdapted" => ascending ? list.OrderBy(x => x.MinAdapted).ToList() : list.OrderByDescending(x => x.MinAdapted).ToList(),
				"AvgAdapted" => ascending ? list.OrderBy(x => x.AvgAdapted).ToList() : list.OrderByDescending(x => x.AvgAdapted).ToList(),
				"MaxAdapted" => ascending ? list.OrderBy(x => x.MaxAdapted).ToList() : list.OrderByDescending(x => x.MaxAdapted).ToList(),
				_ => list
			};

			bindingList.Clear();
			foreach (var item in list)
				bindingList.Add(item);

			dataGridView.Tag = $"{columnName} {(ascending ? "ASC" : "DESC")}";
		}

		private void GraphPanel_VertexSelected(object sender, VertexSelectedEventArgs e)
		{
			if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList) {
				// Create a set of lesson IDs to select
				var lessonIdsToSelect = new HashSet<int>(e.RepresentedLessonIds ?? [e.VertexId]);

				// Clear previous selection
				dataGridView.ClearSelection();

				// Select all rows matching the represented lesson IDs
				for (var i = 0; i < bindingList.Count; i++) {
					if (lessonIdsToSelect.Contains(bindingList[i].LessonId)) {
						dataGridView.Rows[i].Selected = true;
					}
				}

				// Scroll to the first selected row
				if (dataGridView.SelectedRows.Count > 0) {
					var firstIndex = dataGridView.SelectedRows.Cast<DataGridViewRow>().Min(r => r.Index);
					dataGridView.FirstDisplayedScrollingRowIndex = firstIndex;
				}
			}

			UpdateDetailPanel(e.VertexId);
		}

		private void DataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex < 0)
				return;

			if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList) {
				if (e.RowIndex < bindingList.Count) {
					var lesson = bindingList[e.RowIndex];
					var vertexId = graphPanel?.GetRepresentativeVertexId(lesson.LessonId) ?? lesson.LessonId;
					graphPanel?.HighlightVertex(vertexId);
					UpdateDetailPanel(lesson.LessonId);
				}
			}
		}

		private void UpdateDetailPanel(int lessonId)
		{
			if (detailLabel == null)
				return;

			if (orgData != null && orgData.TryGetValue(lessonId, out var info))
				detailLabel.Text = $"Lesson {lessonId}   |   {info.ToDetailString()}";
			else
				detailLabel.Text = lessonId >= 0 ? $"Lesson {lessonId}" : string.Empty;
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			try {
				if (Properties.Settings.Default.FormWidth > 0 && Properties.Settings.Default.FormHeight > 0) {
					Width = Properties.Settings.Default.FormWidth;
					Height = Properties.Settings.Default.FormHeight;
				}

				if (Properties.Settings.Default.FormX > int.MinValue && Properties.Settings.Default.FormY > int.MinValue)
					Location = new Point(Properties.Settings.Default.FormX, Properties.Settings.Default.FormY);

				if (splitContainer != null && Properties.Settings.Default.SplitterDistance > 0)
					splitContainer.SplitterDistance = Properties.Settings.Default.SplitterDistance;
			}
			catch {
				// If settings are invalid or missing, use defaults
			}
		}

		private void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			Properties.Settings.Default.FormWidth = Width;
			Properties.Settings.Default.FormHeight = Height;
			Properties.Settings.Default.FormX = Location.X;
			Properties.Settings.Default.FormY = Location.Y;

			if (splitContainer != null)
				Properties.Settings.Default.SplitterDistance = splitContainer.SplitterDistance;

			Properties.Settings.Default.Save();
		}
	}
}
