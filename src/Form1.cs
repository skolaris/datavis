using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeltaVis
{
	public partial class Form1 : Form
	{
		private OpenFileDialog openFileDialog;
		private DataGridView dataGridView;
		private Button loadFileButton;
		private Button loadPositionsButton;
		private BindingSource bindingSource;
		private Dictionary<int, (int ExecutedMoves, HashSet<string> Timeslots)> positionsData;
		private Dictionary<int, HashSet<int>> graphData; // lessonId -> connected lesson ids
		private Dictionary<int, LessonInfo> orgData;
		private bool hasDeltaData = false;
		private SplitContainer splitContainer;
		private GraphVisualizationPanel graphPanel;
		private Label detailLabel;

		public Form1()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			Text = "Delta Visualization";
			Width = 1400;
			Height = 700;
			StartPosition = FormStartPosition.CenterScreen;

			// Load Deltas Button
			loadFileButton = new Button {
				Text = "Load Deltas",
				Location = new Point(10, 10),
				Size = new Size(100, 30)
			};
			loadFileButton.Click += LoadFileButton_Click;
			Controls.Add(loadFileButton);

			// Load Positions Button
			loadPositionsButton = new Button {
				Text = "Load Positions",
				Location = new Point(120, 10),
				Size = new Size(120, 30)
			};
			loadPositionsButton.Click += LoadPositionsButton_Click;
			Controls.Add(loadPositionsButton);

			// Load Graph Button
			var loadGraphButton = new Button {
				Text = "Load Graph",
				Location = new Point(250, 10),
				Size = new Size(100, 30)
			};
			loadGraphButton.Click += LoadGraphButton_Click;
			Controls.Add(loadGraphButton);

			// Load Data Button
			var loadDataButton = new Button {
				Text = "Load Data",
				Location = new Point(360, 10),
				Size = new Size(100, 30)
			};
			loadDataButton.Click += LoadDataButton_Click;
			Controls.Add(loadDataButton);

			// Split container for grid and graph visualization
			splitContainer = new SplitContainer {
				Location = new Point(10, 50),
				Size = new Size(ClientSize.Width - 20, ClientSize.Height - 60),
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
				Orientation = Orientation.Vertical,
				SplitterDistance = (int)((ClientSize.Width - 20) * 0.6)
			};

			// DataGridView (left panel)
			dataGridView = new DataGridView {
				Location = new Point(0, 0),
				Size = splitContainer.Panel1.ClientSize,
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
				AutoGenerateColumns = false,
				AllowUserToAddRows = false,
				EnableHeadersVisualStyles = true,
				ReadOnly = true,
				RowHeadersVisible = true,
				RowHeadersWidth = 60,
				SelectionMode = DataGridViewSelectionMode.FullRowSelect,
				MultiSelect = true
			};
			dataGridView.ColumnHeaderMouseClick += DataGridView_ColumnHeaderMouseClick;
			dataGridView.RowPostPaint += DataGridView_RowPostPaint;
			dataGridView.CellClick += DataGridView_CellClick;

			// Define columns
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "LessonId", DataPropertyName = "LessonId", HeaderText = "Lesson ID", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrgClassGroups", DataPropertyName = "OrgClassGroups", HeaderText = "Class Groups", Width = 120 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrgModule", DataPropertyName = "OrgModule", HeaderText = "Module", Width = 80 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrgLecturers", DataPropertyName = "OrgLecturers", HeaderText = "Lecturers", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrgSubGroups", DataPropertyName = "OrgSubGroups", HeaderText = "Subgroups", Width = 120 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrgTeachingGroup", DataPropertyName = "OrgTeachingGroup", HeaderText = "Teaching Group", Width = 120 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "EvaluatedMoveCount", DataPropertyName = "EvaluatedMoveCount", HeaderText = "Eval Moves", Width = 80 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExecutedMoveCount", DataPropertyName = "ExecutedMoveCount", HeaderText = "Exec Moves", Width = 80 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "DistinctTimeslots", DataPropertyName = "DistinctTimeslots", HeaderText = "Distinct TS", Width = 80 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "EdgeCount", DataPropertyName = "EdgeCount", HeaderText = "Edges", Width = 60 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MinDelta", DataPropertyName = "MinDelta", HeaderText = "Min Delta", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "AvgDelta", DataPropertyName = "AvgDelta", HeaderText = "Avg Delta", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MaxDelta", DataPropertyName = "MaxDelta", HeaderText = "Max Delta", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MinAdapted", DataPropertyName = "MinAdapted", HeaderText = "Min Adapted", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "AvgAdapted", DataPropertyName = "AvgAdapted", HeaderText = "Avg Adapted", Width = 100 });
			dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MaxAdapted", DataPropertyName = "MaxAdapted", HeaderText = "Max Adapted", Width = 100 });

			splitContainer.Panel1.Controls.Add(dataGridView);

			// Right-hand container: control bar (top) + graph (fill) + detail label (bottom)
			var rightLayout = new TableLayoutPanel {
				Dock = DockStyle.Fill,
				RowCount = 3,
				ColumnCount = 1
			};
			rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));  // control bar
			rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // graph
			rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));  // detail label

			// Graph control bar
			var graphControlBar = new Panel {
				Dock = DockStyle.Fill,
				BackColor = SystemColors.Control,
				Padding = new Padding(4, 3, 4, 3)
			};
			var cbx = 6;
			var chkEdges = new CheckBox { Text = "Show edges", Checked = true, AutoSize = true, Location = new Point(cbx, 5) };
			chkEdges.CheckedChanged += (s, ev) => { if (graphPanel != null) { graphPanel.ShowEdges = chkEdges.Checked; graphPanel.Invalidate(); } };
			graphControlBar.Controls.Add(chkEdges);
			cbx += 110;
			graphControlBar.Controls.Add(new Label { Text = "|", AutoSize = true, Location = new Point(cbx, 5), ForeColor = SystemColors.GrayText });
			cbx += 20;
			var chkModule = new CheckBox { Text = "Color by module", Checked = false, AutoSize = true, Location = new Point(cbx, 5) };
			chkModule.CheckedChanged += (s, ev) => { if (graphPanel != null) { graphPanel.ColorByModule = chkModule.Checked; graphPanel.Invalidate(); } };
			graphControlBar.Controls.Add(chkModule);
			rightLayout.Controls.Add(graphControlBar, 0, 0);

			// Graph Visualization Panel
			graphPanel = new GraphVisualizationPanel {
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle
			};
			graphPanel.VertexSelected += GraphPanel_VertexSelected;
			rightLayout.Controls.Add(graphPanel, 0, 1);

			// Detail label
			detailLabel = new Label {
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				Font = new Font("Segoe UI", 9f),
				Padding = new Padding(4, 0, 0, 0),
				BorderStyle = BorderStyle.FixedSingle
			};
			rightLayout.Controls.Add(detailLabel, 0, 2);

			splitContainer.Panel2.Controls.Add(rightLayout);

			Controls.Add(splitContainer);

			// OpenFileDialog
			openFileDialog = new OpenFileDialog {
				Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
				Title = "Select Data File"
			};

			// Wire up form events for persistence
			Load += Form1_Load;
			FormClosing += Form1_FormClosing;
		}
	}
}
