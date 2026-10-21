using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DeltaVis
{
	public class ScatterPlotPanel : Panel
	{
		public event EventHandler<VertexSelectedEventArgs> VertexSelected;

		public ScatterPlotPanel()
		{
			DoubleBuffered = true;
			BackColor = Color.White;
			MouseClick += ScatterPlotPanel_MouseClick;
			MouseMove += ScatterPlotPanel_MouseMove;
		}

		public void SetLessonStats(IReadOnlyList<LessonStatistics> stats)
		{
			_stats = stats;
			_positionsDirty = true;
			Invalidate();
		}

		public void SetClusterData(
			Dictionary<int, int> vertexToCluster,
			Dictionary<int, Color> clusterColors,
			Dictionary<int, List<int>> mergedToOriginals)
		{
			_vertexToCluster = vertexToCluster;
			_clusterColors = clusterColors;
			_mergedToOriginals = mergedToOriginals;
			_positionsDirty = true;
			Invalidate();
		}

		public void SetLessonTooltips(Dictionary<int, LessonInfo> tooltips) => _lessonTooltips = tooltips;

		public void HighlightVertex(int vertexId) { _selectedVertexId = vertexId; Invalidate(); }

		public void ClearHighlight() { _selectedVertexId = -1; Invalidate(); }

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			var positions = GetPositions(out var minX, out var maxX, out var minY, out var maxY);
			if (positions.Count == 0) {
				using var f = new Font("Arial", 12);
				e.Graphics.DrawString("No data", f, Brushes.Gray, 20, 20);
				return;
			}

			DrawAxes(e.Graphics, minX, maxX, minY, maxY);
			DrawVertices(e.Graphics, positions);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			_positionsDirty = true;
		}

		Dictionary<int, PointF> GetPositions(out float minX, out float maxX, out float minY, out float maxY)
		{
			if (_positionsDirty || _cacheWidth != Width || _cacheHeight != Height) {
				_cachedPositions = ComputePositions(out _cacheMinX, out _cacheMaxX, out _cacheMinY, out _cacheMaxY);
				_cacheWidth = Width;
				_cacheHeight = Height;
				_positionsDirty = false;
			}
			minX = _cacheMinX;
			maxX = _cacheMaxX;
			minY = _cacheMinY;
			maxY = _cacheMaxY;
			return _cachedPositions ?? [];
		}

		Dictionary<int, PointF> ComputePositions(out float minX, out float maxX, out float minY, out float maxY)
		{
			minX = 0;
			maxX = 0;
			minY = 0;
			maxY = 0;

			if (_stats == null || _stats.Count == 0)
				return [];

			Dictionary<int, (float x, float y)> dataPoints;

			if (_vertexToCluster != null) {
				var statsById = _stats.ToDictionary(s => s.LessonId);
				dataPoints = [];
				foreach (var vertexId in _vertexToCluster.Keys) {
					float xVal, yVal;
					if (_mergedToOriginals != null && _mergedToOriginals.TryGetValue(vertexId, out var originals)) {
						var valid = originals
							.Where(statsById.ContainsKey)
							.Select(id => statsById[id])
							.ToList();
						xVal = valid.Count > 0 ? (float)valid.Average(s => s.EdgeCount) : 0f;
						yVal = valid.Count > 0 ? (float)valid.Average(s => s.ExecutedMoveCount) : 0f;
					}
					else {
						statsById.TryGetValue(vertexId, out var stat);
						xVal = stat?.EdgeCount ?? 0f;
						yVal = stat?.ExecutedMoveCount ?? 0f;
					}
					dataPoints[vertexId] = (xVal, yVal);
				}
			}
			else {
				dataPoints = _stats.ToDictionary(
					s => s.LessonId,
					s => ((float)s.EdgeCount, (float)s.ExecutedMoveCount));
			}

			if (dataPoints.Count == 0)
				return [];

			minX = dataPoints.Values.Min(p => p.x);
			maxX = dataPoints.Values.Max(p => p.x);
			minY = dataPoints.Values.Min(p => p.y);
			maxY = dataPoints.Values.Max(p => p.y);
			if (maxX - minX < 1) { minX -= 1; maxX += 1; }
			if (maxY - minY < 1) { minY -= 1; maxY += 1; }

			var plotW = Width - MarginLeft - MarginRight - Radius * 2;
			var plotH = Height - MarginTop - MarginBottom - Radius * 2;
			if (plotW <= 0 || plotH <= 0)
				return [];

			var result = new Dictionary<int, PointF>(dataPoints.Count);
			foreach (var (vid, (xv, yv)) in dataPoints) {
				var cx = MarginLeft + Radius + (xv - minX) / (maxX - minX) * plotW;
				var cy = MarginTop + Radius + (1f - (yv - minY) / (maxY - minY)) * plotH;
				result[vid] = new PointF(cx - Radius, cy - Radius);
			}
			return result;
		}

		void DrawAxes(Graphics g, float minX, float maxX, float minY, float maxY)
		{
			var axisLeft = MarginLeft;
			var axisBottom = Height - MarginBottom;
			var axisRight = Width - MarginRight;
			var axisTop = MarginTop;
			var plotW = axisRight - axisLeft - Radius * 2;
			var plotH = axisBottom - axisTop - Radius * 2;

			g.DrawLine(Pens.Black, axisLeft, axisBottom, axisRight, axisBottom);
			g.DrawLine(Pens.Black, axisLeft, axisTop, axisLeft, axisBottom);

			using var tickFont = new Font("Arial", 7f);
			using var axisLabelFont = new Font("Arial", 8f, FontStyle.Bold);

			foreach (var tick in ComputeTicks(minX, maxX, 6)) {
				var px = axisLeft + Radius + (tick - minX) / (maxX - minX) * plotW;
				g.DrawLine(Pens.Black, px, axisBottom, px, axisBottom + 4);
				var s = ((int)Math.Round(tick)).ToString();
				var sz = g.MeasureString(s, tickFont);
				g.DrawString(s, tickFont, Brushes.Black, px - sz.Width / 2, axisBottom + 5);
			}

			foreach (var tick in ComputeTicks(minY, maxY, 6)) {
				var py = axisTop + Radius + (1f - (tick - minY) / (maxY - minY)) * plotH;
				g.DrawLine(Pens.Black, axisLeft - 4, py, axisLeft, py);
				var s = ((int)Math.Round(tick)).ToString();
				var sz = g.MeasureString(s, tickFont);
				g.DrawString(s, tickFont, Brushes.Black, axisLeft - sz.Width - 5, py - sz.Height / 2);
			}

			var xLbl = "Edges";
			var xLblSz = g.MeasureString(xLbl, axisLabelFont);
			g.DrawString(xLbl, axisLabelFont, Brushes.Black,
				axisLeft + (axisRight - axisLeft) / 2 - xLblSz.Width / 2, axisBottom + 22);

			var state = g.Save();
			g.TranslateTransform(10f, axisTop + (axisBottom - axisTop) / 2f);
			g.RotateTransform(-90f);
			const string yLbl = "Exec Moves";
			var yLblSz = g.MeasureString(yLbl, axisLabelFont);
			g.DrawString(yLbl, axisLabelFont, Brushes.Black, -yLblSz.Width / 2, -yLblSz.Height / 2);
			g.Restore(state);
		}

		static List<float> ComputeTicks(float min, float max, int targetCount)
		{
			var range = max - min;
			if (range <= 0)
				return [];

			var rawStep = range / targetCount;
			var magnitude = (float)Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
			float[] niceMultiples = [1f, 2f, 5f, 10f];
			var step = niceMultiples.Select(m => m * magnitude).First(s => s >= rawStep);
			step = Math.Max(step, 1f);

			var ticks = new List<float>();
			var start = (float)Math.Ceiling(min / step) * step;
			for (var v = start; v <= max + step * 0.001f; v += step)
				ticks.Add(v);
			return ticks;
		}

		void DrawVertices(Graphics g, Dictionary<int, PointF> positions)
		{
			foreach (var (vertexId, pos) in positions) {
				var clusterIdx = _vertexToCluster != null && _vertexToCluster.TryGetValue(vertexId, out var ci) ? ci : -1;
				var color = _clusterColors != null && clusterIdx >= 0 && _clusterColors.TryGetValue(clusterIdx, out var cc)
					? cc : Color.SteelBlue;
				var rect = new RectangleF(pos.X, pos.Y, Radius * 2, Radius * 2);
				using (var brush = new SolidBrush(color))
					g.FillEllipse(brush, rect);
				if (vertexId == _selectedVertexId)
					using (var selPen = new Pen(Color.Black, 2f))
						g.DrawEllipse(selPen, rect);
			}
		}

		void ScatterPlotPanel_MouseClick(object sender, MouseEventArgs e)
		{
			var positions = GetPositions(out _, out _, out _, out _);
			foreach (var (vertexId, pos) in positions) {
				if (!new RectangleF(pos.X, pos.Y, Radius * 2, Radius * 2).Contains(e.Location))
					continue;

				_selectedVertexId = vertexId;
				var representedIds = _mergedToOriginals != null && _mergedToOriginals.TryGetValue(vertexId, out var merged)
					? merged : [vertexId];
				VertexSelected?.Invoke(this, new VertexSelectedEventArgs(vertexId, representedIds));
				Invalidate();
				return;
			}
			_selectedVertexId = -1;
			Invalidate();
		}

		void ScatterPlotPanel_MouseMove(object sender, MouseEventArgs e)
		{
			if (_lessonTooltips == null)
				return;

			var positions = GetPositions(out _, out _, out _, out _);
			foreach (var (vertexId, pos) in positions) {
				if (!new RectangleF(pos.X, pos.Y, Radius * 2, Radius * 2).Contains(e.Location))
					continue;

				if (_lastTooltipVertex == vertexId)
					return;
				_lastTooltipVertex = vertexId;

				var represented = _mergedToOriginals != null && _mergedToOriginals.TryGetValue(vertexId, out var originals)
					? originals : [vertexId];

				var sb = new StringBuilder();
				foreach (var lid in represented) {
					sb.Append($"Lesson {lid}");
					if (_lessonTooltips.TryGetValue(lid, out var info)) {
						var detail = info.ToDetailString();
						if (!string.IsNullOrEmpty(detail))
							sb.Append(": ").Append(detail.Replace("   |   ", ", "));
					}
					sb.AppendLine();
				}
				_toolTip.SetToolTip(this, sb.ToString().TrimEnd());
				return;
			}

			if (_lastTooltipVertex != -1) {
				_lastTooltipVertex = -1;
				_toolTip.SetToolTip(this, string.Empty);
			}
		}

		const float Radius = 6f;
		const float MarginLeft = 60f;
		const float MarginBottom = 40f;
		const float MarginTop = 15f;
		const float MarginRight = 15f;

		IReadOnlyList<LessonStatistics> _stats;
		Dictionary<int, int> _vertexToCluster;
		Dictionary<int, Color> _clusterColors;
		Dictionary<int, List<int>> _mergedToOriginals;
		Dictionary<int, LessonInfo> _lessonTooltips;
		int _selectedVertexId = -1;
		int _lastTooltipVertex = -1;

		Dictionary<int, PointF> _cachedPositions;
		float _cacheMinX, _cacheMaxX, _cacheMinY, _cacheMaxY;
		int _cacheWidth, _cacheHeight;
		bool _positionsDirty = true;

		readonly ToolTip _toolTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };
	}
}
