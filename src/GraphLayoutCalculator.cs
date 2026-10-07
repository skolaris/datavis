using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DeltaVis
{
	/// <summary>
	/// Calculates vertex and cluster positions for graph visualization layout.
	/// Handles ellipse-based cluster placement and directional vertex positioning.
	/// </summary>
	internal class GraphLayoutCalculator
	{
		private const float Padding = 30f;

		/// <summary>
		/// Calculate vertex positions based on clusters and graph connectivity.
		/// </summary>
		public void CalculateLayout(
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, int> vertexToCluster,
			Dictionary<int, PointF> vertexPositions,
			float vertexRadius,
			int panelWidth,
			int panelHeight)
		{
			vertexPositions.Clear();

			if (graphData == null || graphData.Count == 0)
				return;

			var centerX = panelWidth / 2f;
			var centerY = panelHeight / 2f;

			var maxRadiusX = panelWidth / 2f - Padding - vertexRadius;
			var maxRadiusY = panelHeight / 2f - Padding - vertexRadius;

			if (maxRadiusX < vertexRadius)
				maxRadiusX = vertexRadius + 10;
			if (maxRadiusY < vertexRadius)
				maxRadiusY = vertexRadius + 10;

			var clusterCount = clusters.Count;

			// Calculate inter-cluster connections for each vertex
			Dictionary<int, Dictionary<int, int>> vertexInterClusterEdges = [];
			foreach (var vertex in graphData.Keys) {
				vertexInterClusterEdges[vertex] = [];
				var sourceCluster = vertexToCluster[vertex];
				foreach (var target in graphData[vertex]) {
					var targetCluster = vertexToCluster[target];
					if (targetCluster != sourceCluster) {
						if (!vertexInterClusterEdges[vertex].ContainsKey(targetCluster))
							vertexInterClusterEdges[vertex][targetCluster] = 0;
						vertexInterClusterEdges[vertex][targetCluster]++;
					}
				}
			}

			// Pass 1: compute cluster radii and initial centers on a ring
			var clusterRadii = new float[clusterCount];
			var clusterCX = new float[clusterCount];
			var clusterCY = new float[clusterCount];
			var ringRadius = 0.5f * Math.Max(maxRadiusX, maxRadiusY);

			for (var i = 0; i < clusterCount; i++) {
				clusterRadii[i] = 2.4f * vertexRadius * (float)Math.Sqrt(clusters[i].Count / Math.PI);
				var angle = (float)i / clusterCount * 2 * (float)Math.PI;
				clusterCX[i] = centerX + ringRadius * (float)Math.Cos(angle);
				clusterCY[i] = centerY + ringRadius * (float)Math.Sin(angle);
			}

			// Pass 2: iteratively push overlapping clusters apart until none overlap
			var gap = vertexRadius * 2f;
			for (var iter = 0; iter < 500; iter++) {
				var moved = false;
				for (var a = 0; a < clusterCount; a++) {
					for (var b = a + 1; b < clusterCount; b++) {
						var dx = clusterCX[b] - clusterCX[a];
						var dy = clusterCY[b] - clusterCY[a];
						var minDist = clusterRadii[a] + clusterRadii[b] + gap;
						var dist = (float)Math.Sqrt(dx * dx + dy * dy);

						if (dist < minDist) {
							moved = true;
							if (dist < 0.001f) { dx = 1f; dy = 0f; dist = 1f; }
							var push = (minDist - dist) * 0.5f;
							var nx = dx / dist;
							var ny = dy / dist;
							clusterCX[a] -= nx * push;
							clusterCY[a] -= ny * push;
							clusterCX[b] += nx * push;
							clusterCY[b] += ny * push;
						}
					}
				}
				if (!moved)
					break;
			}

			// Pass 3: scale and centre the layout so all clusters fit within the panel
			{
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;
				for (var i = 0; i < clusterCount; i++) {
					minX = Math.Min(minX, clusterCX[i] - clusterRadii[i]);
					maxX = Math.Max(maxX, clusterCX[i] + clusterRadii[i]);
					minY = Math.Min(minY, clusterCY[i] - clusterRadii[i]);
					maxY = Math.Max(maxY, clusterCY[i] + clusterRadii[i]);
				}

				var usedWidth = maxX - minX;
				var usedHeight = maxY - minY;
				var availWidth = panelWidth - 2 * Padding;
				var availHeight = panelHeight - 2 * Padding;

				// Scale down only — do not enlarge (would cause intra-cluster vertex overlap)
				var scale = Math.Min(1f, Math.Min(availWidth / usedWidth, availHeight / usedHeight));

				var layoutCX = (minX + maxX) / 2f;
				var layoutCY = (minY + maxY) / 2f;

				for (var i = 0; i < clusterCount; i++) {
					clusterCX[i] = centerX + (clusterCX[i] - layoutCX) * scale;
					clusterCY[i] = centerY + (clusterCY[i] - layoutCY) * scale;
					clusterRadii[i] *= scale;
				}
			}

			// Pass 4: position vertices within each cluster using Fibonacci disc pattern
			var goldenAngle = (float)(Math.PI * (3.0 - Math.Sqrt(5.0)));

			for (var clusterIdx = 0; clusterIdx < clusterCount; clusterIdx++) {
				List<int> cluster = clusters[clusterIdx];
				var verticesInCluster = cluster.Count;
				var clusterCenterX = clusterCX[clusterIdx];
				var clusterCenterY = clusterCY[clusterIdx];
				var clusterRadius = clusterRadii[clusterIdx];

				// Compute per-vertex external connection directions
				List<(int vertex, float angle, float externalStrength)> positionedVertices = [];

				foreach (var vertexId in cluster) {
					var totalXDirection = 0f;
					var totalYDirection = 0f;
					var totalExternalEdges = 0f;

					if (vertexInterClusterEdges[vertexId].Count > 0) {
						foreach (var kvp in vertexInterClusterEdges[vertexId]) {
							var targetCluster = kvp.Key;
							var edgeCount = kvp.Value;

							var targetClusterAngle = (float)targetCluster / clusterCount * 2 * (float)Math.PI;
							totalXDirection += edgeCount * (float)Math.Cos(targetClusterAngle);
							totalYDirection += edgeCount * (float)Math.Sin(targetClusterAngle);
							totalExternalEdges += edgeCount;
						}
					}

					var directionAngle = totalExternalEdges > 0
						? (float)Math.Atan2(totalYDirection, totalXDirection)
						: 0f;

					positionedVertices.Add((vertexId, directionAngle, totalExternalEdges));
				}

				// Generate Fibonacci (sunflower) disc positions — purely uniform, no per-vertex distortion
				var discPositions = new List<(float x, float y, float r)>(verticesInCluster);
				for (var k = 0; k < verticesInCluster; k++) {
					var r = clusterRadius * (float)Math.Sqrt((k + 0.5f) / verticesInCluster);
					var a = k * goldenAngle;
					discPositions.Add((r * (float)Math.Cos(a), r * (float)Math.Sin(a), r));
				}

				// Inner positions → vertices with no external connections
				// Outer positions → vertices with external connections, matched by direction angle
				var sortedByRadius = discPositions.OrderBy(p => p.r).ToList();
				var innerCount = positionedVertices.Count(v => v.externalStrength == 0);

				var innerPositions = sortedByRadius.Take(innerCount).ToList();
				var outerPositions = sortedByRadius.Skip(innerCount)
					.OrderBy(p => (float)Math.Atan2(p.y, p.x)).ToList();

				var noExternalVerts = positionedVertices.Where(v => v.externalStrength == 0).ToList();
				var externalVerts = positionedVertices
					.Where(v => v.externalStrength > 0)
					.OrderBy(v => v.angle).ToList();

				for (var i = 0; i < noExternalVerts.Count; i++) {
					var pos = innerPositions[i];
					vertexPositions[noExternalVerts[i].vertex] = new PointF(
						clusterCenterX + pos.x - vertexRadius,
						clusterCenterY + pos.y - vertexRadius);
				}

				for (var i = 0; i < externalVerts.Count; i++) {
					var pos = outerPositions[i];
					vertexPositions[externalVerts[i].vertex] = new PointF(
						clusterCenterX + pos.x - vertexRadius,
						clusterCenterY + pos.y - vertexRadius);
				}
			}
		}
	}
}
