using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeltaVis
{
	public partial class Form1
	{
		ScatterPlotPanel scatterPanel;
		ComboBox graphViewDropdown;

		void InitializeScatterPlotPanel(Panel controlBar, TableLayoutPanel rightLayout)
		{
			graphViewDropdown = new ComboBox {
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 175,
				Location = new Point(0, 3)
			};
			graphViewDropdown.Items.AddRange(["Dependencies", "Edge moves correlation"]);
			graphViewDropdown.SelectedIndex = 0;
			graphViewDropdown.SelectedIndexChanged += (s, ev) => {
				graphPanel.Visible = graphViewDropdown.SelectedIndex == 0;
				scatterPanel.Visible = graphViewDropdown.SelectedIndex == 1;
			};
			controlBar.Controls.Add(graphViewDropdown);
			controlBar.Layout += (s, ev) =>
				graphViewDropdown.Left = controlBar.ClientSize.Width - graphViewDropdown.Width - 4;

			scatterPanel = new ScatterPlotPanel {
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Visible = false
			};
			scatterPanel.VertexSelected += ScatterPanel_VertexSelected;
			rightLayout.Controls.Add(scatterPanel, 0, 1);
		}

		void ScatterPanel_VertexSelected(object sender, VertexSelectedEventArgs e)
		{
			if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList) {
				var lessonIdsToSelect = new HashSet<int>(e.RepresentedLessonIds ?? [e.VertexId]);
				dataGridView.ClearSelection();
				for (var i = 0; i < bindingList.Count; i++) {
					if (lessonIdsToSelect.Contains(bindingList[i].LessonId))
						dataGridView.Rows[i].Selected = true;
				}
				if (dataGridView.SelectedRows.Count > 0) {
					var firstIndex = dataGridView.SelectedRows.Cast<DataGridViewRow>().Min(r => r.Index);
					dataGridView.FirstDisplayedScrollingRowIndex = firstIndex;
				}
			}
			graphPanel?.HighlightVertex(e.VertexId);
			UpdateDetailPanel(e.VertexId);
		}

		void GraphPanel_LayoutComplete(object sender, System.EventArgs e)
		{
			scatterPanel?.SetClusterData(
				graphPanel.CachedVertexToCluster,
				graphPanel.CachedClusterColors,
				graphPanel.MergedToOriginals);
		}

		void RefreshScatterPlotStats()
		{
			if (bindingSource?.DataSource is BindingList<LessonStatistics> bindingList)
				scatterPanel?.SetLessonStats([.. bindingList]);
		}
	}
}
