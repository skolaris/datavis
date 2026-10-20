using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeltaVis
{
	/// <summary>
	/// Handles rendering of graph visualization including vertices, edges, and cluster boundaries.
	/// </summary>
	internal class GraphRenderer
	{
		private float vertexRadius;
		private int maxEdgeCount;

		public void SetVertexRadius(float radius)
		{
			vertexRadius = radius;
		}

		public void SetMaxEdgeCount(int maxEdges)
		{
			maxEdgeCount = maxEdges;
		}

		/// <summary>
		/// Calculate a color based on edge count intensity.
		/// More edges = more saturated/intense color.
		/// Uses non-linear scaling for stronger visual differentiation.
		/// </summary>
		public Color GetColorByEdgeIntensity(int vertexId, Color baseColor, Dictionary<int, HashSet<int>> graphData)
		{
			if (!graphData.TryGetValue(vertexId, out var edges))
				return baseColor;

			var edgeCount = edges.Count;
			var intensity = maxEdgeCount > 0 ? (float)edgeCount / maxEdgeCount : 0f;

			// Apply non-linear scaling (power of 1.5) for stronger contrast
			// This makes the difference between low and high edge counts much more visible
			intensity = (float)Math.Pow(intensity, 1.5f);

			// Create a much paler version (only 15% of base color, 85% white) for low-edge vertices
			var pale_r = (int)(255 + (baseColor.R - 255) * 0.15f);
			var pale_g = (int)(255 + (baseColor.G - 255) * 0.15f);
			var pale_b = (int)(255 + (baseColor.B - 255) * 0.15f);

			// Interpolate between pale and base color based on computed intensity
			var r = (int)(pale_r + (baseColor.R - pale_r) * intensity);
			var g = (int)(pale_g + (baseColor.G - pale_g) * intensity);
			var b = (int)(pale_b + (baseColor.B - pale_b) * intensity);

			return Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
		}

		/// <summary>
		/// Draw cluster boundaries as rectangles around each cluster's vertices.
		/// </summary>
		private void DrawClusterBoundaries(
			PaintEventArgs e,
			List<List<int>> clusters,
			Dictionary<int, PointF> vertexPositions,
			Dictionary<int, Color> clusterColors,
			Dictionary<int, LessonInfo> lessonInfo = null)
		{
			if (clusters == null || clusters.Count == 0)
				return;

			using var labelFont = new Font("Arial", 7.5f, FontStyle.Bold);

			for (var clusterIdx = 0; clusterIdx < clusters.Count; clusterIdx++) {
				var cluster = clusters[clusterIdx];
				if (cluster.Count == 0)
					continue;

				// Find bounding box for this cluster
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;

				foreach (var vertexId in cluster) {
					if (vertexPositions.TryGetValue(vertexId, out var pos)) {
						minX = Math.Min(minX, pos.X);
						maxX = Math.Max(maxX, pos.X + vertexRadius * 2);
						minY = Math.Min(minY, pos.Y);
						maxY = Math.Max(maxY, pos.Y + vertexRadius * 2);
					}
				}

				if (minX == float.MaxValue)
					continue;

				// Draw bounding box with cluster color, semi-transparent
				var clusterColor = clusterColors.TryGetValue(clusterIdx, out var cColor) ? cColor : Color.SteelBlue;

				// Add padding around bounding box
				var padding = 15f;
				RectangleF boundingBox = new RectangleF(
					minX - padding, minY - padding,
					maxX - minX + 2 * padding, maxY - minY + 2 * padding
				);

				using (var boundaryPen = new Pen(clusterColor, 2f)) {
					e.Graphics.DrawRectangle(boundaryPen, boundingBox.X, boundingBox.Y, boundingBox.Width, boundingBox.Height);
				}

				// Draw theme label inside top-left corner
				if (lessonInfo != null) {
					var label = ClusterThemeResolver.Resolve(cluster, lessonInfo);
					if (label != null) {
						var textSize = e.Graphics.MeasureString(label, labelFont);
						var labelX = boundingBox.X + 4f;
						var labelY = boundingBox.Y + 4f;
						var bgRect = new RectangleF(labelX - 1f, labelY - 1f, textSize.Width + 2f, textSize.Height + 2f);
						using var bgBrush = new SolidBrush(Color.FromArgb(200, Color.White));
						e.Graphics.FillRectangle(bgBrush, bgRect);
						e.Graphics.DrawString(label, labelFont, Brushes.Black, labelX, labelY);
					}
				}
			}
		}

		/// <summary>
		/// Render the complete graph visualization.
		/// </summary>
		public void Render(
			PaintEventArgs e,
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, PointF> vertexPositions,
			Dictionary<int, Color> clusterColors,
			Dictionary<int, int> vertexToCluster,
			int selectedVertexId,
			int panelWidth,
			int panelHeight,
			bool showEdges = true,
			Dictionary<int, Color> moduleColors = null,
			Dictionary<int, LessonInfo> lessonInfo = null,
			Dictionary<int, int> nodeWeight = null,
			Dictionary<int, List<int>> mergedToOriginals = null,
			int origLessonCount = -1,
			int origTotalEdges = -1,
			int origSelectedEdges = -1)
		{
			if (graphData == null || graphData.Count == 0) {
				using (var font = new Font("Arial", 12)) {
					e.Graphics.DrawString("No graph data loaded", font, Brushes.Gray, 20, 20);
				}
				return;
			}

			// Draw cluster boundaries first (background layer)
			DrawClusterBoundaries(e, clusters, vertexPositions, clusterColors, lessonInfo);

			// Get all neighbours of the selected vertex (both directions)
			var connectedNodes = new HashSet<int>();
			if (selectedVertexId >= 0) {
				if (graphData.TryGetValue(selectedVertexId, out var outEdges))
					foreach (var id in outEdges)
						connectedNodes.Add(id);

				foreach (var kvp in graphData)
					if (kvp.Key != selectedVertexId && kvp.Value.Contains(selectedVertexId))
						connectedNodes.Add(kvp.Key);
			}

			// Lessons sharing the same block as the selected vertex (works across merged nodes too)
			var blockMates = new HashSet<int>();
			if (selectedVertexId >= 0 && lessonInfo != null) {
				// Determine the block of the selected node (may be a merged representative)
				var selIds = mergedToOriginals != null && mergedToOriginals.TryGetValue(selectedVertexId, out var selOriginals)
					? selOriginals
					: [selectedVertexId];
				var selBlockIds = selIds
					.Where(id => lessonInfo.TryGetValue(id, out var i) && i.BlockId != 0)
					.Select(id => lessonInfo[id].BlockId)
					.ToHashSet();

				if (selBlockIds.Count > 0) {
					foreach (var kvp in vertexPositions) {
						if (kvp.Key == selectedVertexId)
							continue;
						var repIds = mergedToOriginals != null && mergedToOriginals.TryGetValue(kvp.Key, out var repOriginals)
							? repOriginals
							: [kvp.Key];
						if (repIds.Any(id => lessonInfo.TryGetValue(id, out var i) && selBlockIds.Contains(i.BlockId)))
							blockMates.Add(kvp.Key);
					}
				}
			}

			// Draw edges to/from selected vertex in light grey
			if (showEdges && connectedNodes.Count > 0 && vertexPositions.TryGetValue(selectedVertexId, out var selPos)) {
				var selR = ActualRadius(selectedVertexId, nodeWeight, vertexRadius);
				var selCenter = new PointF(selPos.X + selR, selPos.Y + selR);
				using var edgePen = new Pen(Color.FromArgb(180, 180, 180), 1.5f);
				foreach (var neighbourId in connectedNodes) {
					if (!vertexPositions.TryGetValue(neighbourId, out var nPos))
						continue;
					var nR = ActualRadius(neighbourId, nodeWeight, vertexRadius);
					var nCenter = new PointF(nPos.X + nR, nPos.Y + nR);
					e.Graphics.DrawLine(edgePen, selCenter, nCenter);
				}
			}

			// Draw vertices
			foreach (var kvp in vertexPositions) {
				var vertexId = kvp.Key;
				var pos = kvp.Value;
				var r = ActualRadius(vertexId, nodeWeight, vertexRadius);
				var w = nodeWeight != null && nodeWeight.TryGetValue(vertexId, out var ww) ? ww : 1;

				var isSelected = selectedVertexId == vertexId;
				var isConnected = showEdges && connectedNodes.Contains(vertexId);
				var isBlockMate = blockMates.Contains(vertexId);

				var clusterId = vertexToCluster.TryGetValue(vertexId, out var cId) ? cId : 0;
				var clusterBaseColor = clusterColors.TryGetValue(clusterId, out var cColor) ? cColor : Color.SteelBlue;

				Brush fillBrush;
				Pen outlinePen;

				if (isSelected) {
					fillBrush = new SolidBrush(clusterBaseColor);
					outlinePen = new Pen(Color.Black, 1f);
				}
				else if (isBlockMate) {
					fillBrush = new SolidBrush(Color.FromArgb(180, 230, 180));
					outlinePen = new Pen(Color.SeaGreen, 2f);
				}
				else if (isConnected) {
					fillBrush = new SolidBrush(GetColorByEdgeIntensity(vertexId, Color.FromArgb(180, 180, 180), graphData));
					outlinePen = new Pen(Color.DimGray, 2f);
				}
				else {
					var baseColor = moduleColors != null && moduleColors.TryGetValue(vertexId, out var modColor)
						? modColor
						: GetColorByEdgeIntensity(vertexId, clusterBaseColor, graphData);
					fillBrush = new SolidBrush(baseColor);
					outlinePen = new Pen(Color.Black, 1f);
				}

				if (isSelected) {
					var isAlreadyLarger = isBlockMate || (w > 1);
					var selR = isAlreadyLarger ? r : r * 1.5f;
					var selX = pos.X + r - selR;
					var selY = pos.Y + r - selR;
					e.Graphics.FillEllipse(Brushes.Black, selX, selY, selR * 2, selR * 2);
					using var selPen = new Pen(Color.White, 2f);
					e.Graphics.DrawEllipse(selPen, selX, selY, selR * 2, selR * 2);
				}
				else {
					e.Graphics.FillEllipse(fillBrush, pos.X, pos.Y, r * 2, r * 2);
					e.Graphics.DrawEllipse(outlinePen, pos.X, pos.Y, r * 2, r * 2);
				}

				// Draw count label for merged nodes (semi-transparent, inside the circle)
				if (w > 1) {
					var label = w.ToString();
					var fontSize = Math.Max(6f, Math.Min(r * 0.9f, 14f));
					using var countFont = new Font("Arial", fontSize, FontStyle.Bold);
					using var countBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
					var textSize = e.Graphics.MeasureString(label, countFont);
					e.Graphics.DrawString(label, countFont, countBrush,
						pos.X + r - textSize.Width / 2,
						pos.Y + r - textSize.Height / 2);
				}

				if (fillBrush is SolidBrush sb)
					sb.Dispose();
				outlinePen.Dispose();
			}

			// Draw selected-vertex label on top (second pass)
			if (selectedVertexId >= 0 && vertexPositions.TryGetValue(selectedVertexId, out var selLabelPos)) {
				var r = ActualRadius(selectedVertexId, nodeWeight, vertexRadius);
				var text = selectedVertexId.ToString();
				using var textFont = new Font("Arial", 7, FontStyle.Bold);
				var textSize = e.Graphics.MeasureString(text, textFont);
				var textX = selLabelPos.X + r - textSize.Width / 2;
				var textY = selLabelPos.Y + r - textSize.Height / 2;
				var pad = 1f;
				e.Graphics.FillRectangle(Brushes.White, textX - pad, textY - pad, textSize.Width + pad * 2, textSize.Height + pad * 2);
				e.Graphics.DrawString(text, textFont, Brushes.Black, textX, textY);
			}

			// Draw info text
			using (var infoFont = new Font("Arial", 9)) {
				var displayLessons = origLessonCount >= 0 ? origLessonCount : graphData.Count;
				var displayEdges = origTotalEdges >= 0 ? origTotalEdges : graphData.Sum(x => x.Value.Count);
				var info = $"Lessons: {displayLessons} | Total Edges: {displayEdges}";
				if (selectedVertexId >= 0) {
					var edgeDisplay = origSelectedEdges >= 0 ? origSelectedEdges : connectedNodes.Count;
					info += $" | Selected: {selectedVertexId} ({edgeDisplay} edges)";
				}
				e.Graphics.DrawString(info, infoFont, Brushes.Black, 10, panelHeight - 25);
			}
		}

		static float ActualRadius(int vertexId, Dictionary<int, int> nodeWeight, float baseRadius)
		{
			if (nodeWeight != null && nodeWeight.TryGetValue(vertexId, out var w) && w > 1)
				return baseRadius * (float)Math.Sqrt(w);
			return baseRadius;
		}
	}
}
