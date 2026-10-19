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
		/// When <paramref name="blockGroups"/> is provided, vertices sharing a block are
		/// placed in consecutive Fibonacci disc slots so they appear physically adjacent.
		/// </summary>
		public void CalculateLayout(
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, int> vertexToCluster,
			Dictionary<int, PointF> vertexPositions,
			float vertexRadius,
			int panelWidth,
			int panelHeight,
			Dictionary<int, int> blockGroups = null)
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
				var vertexAngle = new Dictionary<int, float>();
				var vertexStrength = new Dictionary<int, float>();

				foreach (var vertexId in cluster) {
					var totalX = 0f;
					var totalY = 0f;
					var totalEdges = 0f;

					if (vertexInterClusterEdges[vertexId].Count > 0) {
						foreach (var kvp in vertexInterClusterEdges[vertexId]) {
							var edgeCount = kvp.Value;
							var targetAngle = (float)kvp.Key / clusterCount * 2 * (float)Math.PI;
							totalX += edgeCount * (float)Math.Cos(targetAngle);
							totalY += edgeCount * (float)Math.Sin(targetAngle);
							totalEdges += edgeCount;
						}
					}

					vertexAngle[vertexId] = totalEdges > 0 ? (float)Math.Atan2(totalY, totalX) : 0f;
					vertexStrength[vertexId] = totalEdges;
				}

				// Generate all Fibonacci disc positions
				var discPositions = new List<(float x, float y, float r)>(verticesInCluster);
				for (var k = 0; k < verticesInCluster; k++) {
					var r = clusterRadius * (float)Math.Sqrt((k + 0.5f) / verticesInCluster);
					var a = k * goldenAngle;
					discPositions.Add((r * (float)Math.Cos(a), r * (float)Math.Sin(a), r));
				}

				// Assign disc slots: if block data is available, reserve spatially-contiguous
				// slots for each multi-vertex block before assigning the remaining vertices.
				var slotTaken = new bool[verticesInCluster];
				var assignment = new Dictionary<int, int>(); // vertexId -> disc slot index

				if (blockGroups != null) {
					// Group cluster vertices by block, keeping only blocks with ≥2 members
					var blockMembers = new Dictionary<int, List<int>>();
					foreach (var v in cluster) {
						if (!blockGroups.TryGetValue(v, out var bid))
							continue;
						if (!blockMembers.TryGetValue(bid, out var list)) {
							list = [];
							blockMembers[bid] = list;
						}
						list.Add(v);
					}

					foreach (var (bid, members) in blockMembers) {
						if (members.Count < 2)
							continue;

						// Choose anchor direction as the average external-connection angle of the group
						var ax = members.Average(v => (double)Math.Cos(vertexAngle[v]));
						var ay = members.Average(v => (double)Math.Sin(vertexAngle[v]));
						var groupAngle = (float)Math.Atan2(ay, ax);

						// Pick the disc position closest in angular direction to groupAngle as anchor
						var anchorSlot = -1;
						var bestDot = float.MinValue;
						for (var k = 0; k < verticesInCluster; k++) {
							if (slotTaken[k]) continue;
							var slotAngle = (float)Math.Atan2(discPositions[k].y, discPositions[k].x);
							var dot = (float)(Math.Cos(groupAngle) * Math.Cos(slotAngle) + Math.Sin(groupAngle) * Math.Sin(slotAngle));
							if (dot > bestDot) { bestDot = dot; anchorSlot = k; }
						}

						if (anchorSlot < 0) continue;

						// Find the nearest 'members.Count' free slots to the anchor position
						var anchorX = discPositions[anchorSlot].x;
						var anchorY = discPositions[anchorSlot].y;
						var nearest = Enumerable.Range(0, verticesInCluster)
							.Where(k => !slotTaken[k])
							.OrderBy(k => {
								var dx = discPositions[k].x - anchorX;
								var dy = discPositions[k].y - anchorY;
								return dx * dx + dy * dy;
							})
							.Take(members.Count)
							.ToList();

						// Assign members to those slots ordered by external angle
						var sortedMembers = members.OrderBy(v => vertexAngle[v]).ToList();
						var sortedSlots   = nearest.OrderBy(k => (float)Math.Atan2(discPositions[k].y, discPositions[k].x)).ToList();
						for (var i = 0; i < sortedMembers.Count; i++) {
							assignment[sortedMembers[i]] = sortedSlots[i];
							slotTaken[sortedSlots[i]] = true;
						}
					}
				}

				// Assign remaining vertices (not yet placed) to the free slots.
				// Vertices with external connections get outer (higher-radius) slots matched by angle;
				// vertices without get inner slots.
				var freeSlots = Enumerable.Range(0, verticesInCluster).Where(k => !slotTaken[k]).ToList();
				var unassigned = cluster.Where(v => !assignment.ContainsKey(v)).ToList();

				var innerFree = freeSlots.OrderBy(k => discPositions[k].r).ToList();
				var outerFree = freeSlots.OrderByDescending(k => discPositions[k].r)
					.OrderBy(k => (float)Math.Atan2(discPositions[k].y, discPositions[k].x))
					.ToList();

				var noExternal = unassigned.Where(v => vertexStrength[v] == 0).ToList();
				var hasExternal = unassigned.Where(v => vertexStrength[v] > 0)
					.OrderBy(v => vertexAngle[v]).ToList();

				// Distribute free slots between the two groups
				var innerNeeded = noExternal.Count;
				var innerSlots = innerFree.Take(innerNeeded).ToList();
				var outerSlots = innerFree.Skip(innerNeeded)
					.OrderBy(k => (float)Math.Atan2(discPositions[k].y, discPositions[k].x))
					.ToList();

				for (var i = 0; i < noExternal.Count; i++)
					assignment[noExternal[i]] = innerSlots[i];

				for (var i = 0; i < hasExternal.Count && i < outerSlots.Count; i++)
					assignment[hasExternal[i]] = outerSlots[i];

				// Write final positions
				foreach (var (vertexId, slot) in assignment) {
					var pos = discPositions[slot];
					vertexPositions[vertexId] = new PointF(
						clusterCenterX + pos.x - vertexRadius,
						clusterCenterY + pos.y - vertexRadius);
				}
			}
		}
	}
}
