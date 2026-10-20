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
	class GraphLayoutCalculator
	{
		/// <summary>
		/// Calculate vertex positions based on clusters and graph connectivity.
		/// When <paramref name="blockGroups"/> is provided, vertices sharing a block are
		/// placed in consecutive Fibonacci disc slots so they appear physically adjacent.
		/// </summary>
		public void CalculateLayout(
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, int> vertexToCluster,
			float vertexRadius,
			int panelWidth,
			int panelHeight,
			Dictionary<int, int> blockGroups = null,
			Dictionary<int, int> nodeWeights = null)
		{
			_clusters = clusters;
			_vertexRadius = vertexRadius;
			_nodeWeights = nodeWeights;
			_vertexPositions.Clear();

			if (graphData == null || graphData.Count == 0)
				return;

			var centerX = panelWidth / 2f;
			var centerY = panelHeight / 2f;

			var maxRadiusX = Math.Max(panelWidth / 2f - Padding - vertexRadius, vertexRadius + 10);
			var maxRadiusY = Math.Max(panelHeight / 2f - Padding - vertexRadius, vertexRadius + 10);

			var interClusterEdges = BuildInterClusterEdges(graphData, vertexToCluster);

			ComputeClusterCenters(centerX, centerY, maxRadiusX, maxRadiusY,
				out var clusterRadii, out var clusterCX, out var clusterCY);

			SeparateClusters(clusterCX, clusterCY, clusterRadii, vertexRadius * 2f);

			ScaleAndCenterLayout(clusterCX, clusterCY, clusterRadii, centerX, centerY,
				panelWidth - 2 * Padding, panelHeight - 2 * Padding);

			for (var i = 0; i < _clusters.Count; i++) {
				PositionClusterVertices(clusters[i], clusterCX[i], clusterCY[i], clusterRadii[i],
					interClusterEdges, blockGroups);
			}
			SeparateOverlappingVertices();
		}

		/// <summary>
		/// Counts inter-cluster edges per vertex: vertexId → (targetClusterId → edgeCount).
		/// </summary>
		static Dictionary<int, Dictionary<int, int>> BuildInterClusterEdges(
			Dictionary<int, HashSet<int>> graphData,
			Dictionary<int, int> vertexToCluster)
		{
			Dictionary<int, Dictionary<int, int>> result = [];
			foreach (var vertex in graphData.Keys) {
				result[vertex] = [];
				var sourceCluster = vertexToCluster[vertex];
				foreach (var target in graphData[vertex]) {
					var targetCluster = vertexToCluster[target];
					if (targetCluster == sourceCluster)
						continue;
					result[vertex].TryGetValue(targetCluster, out var count);
					result[vertex][targetCluster] = count + 1;
				}
			}
			return result;
		}

		/// <summary>
		/// Sizes each cluster radius from its total vertex area and places cluster centers on a ring.
		/// </summary>
		void ComputeClusterCenters(
			float centerX, float centerY,
			float maxRadiusX, float maxRadiusY,
			out float[] clusterRadii,
			out float[] clusterCX,
			out float[] clusterCY)
		{
			var n = _clusters.Count;
			clusterRadii = new float[n];
			clusterCX = new float[n];
			clusterCY = new float[n];
			var ringRadius = 0.5f * Math.Max(maxRadiusX, maxRadiusY);

			for (var i = 0; i < n; i++) {
				var totalArea = _clusters[i].Sum(v => {
					var w = _nodeWeights != null && _nodeWeights.TryGetValue(v, out var ww) ? ww : 1;
					var r = _vertexRadius * (float)Math.Sqrt(w);
					return r * r;
				});
				clusterRadii[i] = 2.4f * (float)Math.Sqrt(totalArea / Math.PI);
				var angle = (float)i / n * 2 * (float)Math.PI;
				clusterCX[i] = centerX + ringRadius * (float)Math.Cos(angle);
				clusterCY[i] = centerY + ringRadius * (float)Math.Sin(angle);
			}
		}

		/// <summary>
		/// Iteratively pushes overlapping cluster circles apart until none overlap.
		/// </summary>
		static void SeparateClusters(float[] clusterCX, float[] clusterCY, float[] clusterRadii, float gap)
		{
			var n = clusterCX.Length;
			for (var iter = 0; iter < 500; iter++) {
				var moved = false;
				for (var a = 0; a < n; a++) {
					for (var b = a + 1; b < n; b++) {
						var dx = clusterCX[b] - clusterCX[a];
						var dy = clusterCY[b] - clusterCY[a];
						var minDist = clusterRadii[a] + clusterRadii[b] + gap;
						var dist = (float)Math.Sqrt(dx * dx + dy * dy);
						if (dist >= minDist)
							continue;
						moved = true;
						if (dist < 0.001f) { dx = 1f; dy = 0f; dist = 1f; }
						var push = (minDist - dist) * 0.5f / dist;
						clusterCX[a] -= dx * push;
						clusterCY[a] -= dy * push;
						clusterCX[b] += dx * push;
						clusterCY[b] += dy * push;
					}
				}
				if (!moved)
					break;
			}
		}

		/// <summary>
		/// Scales down and re-centres the cluster layout so all clusters fit within the panel.
		/// </summary>
		static void ScaleAndCenterLayout(
			float[] clusterCX, float[] clusterCY, float[] clusterRadii,
			float centerX, float centerY,
			float availWidth, float availHeight)
		{
			var n = clusterCX.Length;
			float minX = float.MaxValue, maxX = float.MinValue;
			float minY = float.MaxValue, maxY = float.MinValue;
			for (var i = 0; i < n; i++) {
				minX = Math.Min(minX, clusterCX[i] - clusterRadii[i]);
				maxX = Math.Max(maxX, clusterCX[i] + clusterRadii[i]);
				minY = Math.Min(minY, clusterCY[i] - clusterRadii[i]);
				maxY = Math.Max(maxY, clusterCY[i] + clusterRadii[i]);
			}

			// Scale down only — enlarging would push intra-cluster vertices apart
			var scale = Math.Min(1f, Math.Min(availWidth / (maxX - minX), availHeight / (maxY - minY)));
			var layoutCX = (minX + maxX) / 2f;
			var layoutCY = (minY + maxY) / 2f;

			for (var i = 0; i < n; i++) {
				clusterCX[i] = centerX + (clusterCX[i] - layoutCX) * scale;
				clusterCY[i] = centerY + (clusterCY[i] - layoutCY) * scale;
				clusterRadii[i] *= scale;
			}
		}

		/// <summary>
		/// Places the vertices of one cluster into a Fibonacci disc, reserving contiguous slots
		/// for same-block groups and pushing externally-connected vertices toward the cluster edge.
		/// </summary>
		void PositionClusterVertices(
			List<int> cluster,
			float clusterCenterX, float clusterCenterY, float clusterRadius,
			Dictionary<int, Dictionary<int, int>> interClusterEdges,
			Dictionary<int, int> blockGroups)
		{
			var clusterCount = _clusters.Count;
			var n = cluster.Count;
			if (n == 0)
				return;

			// Per-vertex external-connection angle and strength
			var vertexAngle = new Dictionary<int, float>(n);
			var vertexStrength = new Dictionary<int, float>(n);
			foreach (var vertexId in cluster) {
				var totalX = 0f;
				var totalY = 0f;
				var totalEdges = 0f;
				foreach (var kvp in interClusterEdges[vertexId]) {
					var targetAngle = (float)kvp.Key / clusterCount * 2 * (float)Math.PI;
					totalX += kvp.Value * (float)Math.Cos(targetAngle);
					totalY += kvp.Value * (float)Math.Sin(targetAngle);
					totalEdges += kvp.Value;
				}
				vertexAngle[vertexId] = totalEdges > 0 ? (float)Math.Atan2(totalY, totalX) : 0f;
				vertexStrength[vertexId] = totalEdges;
			}

			// Fibonacci disc positions
			var goldenAngle = (float)(Math.PI * (3.0 - Math.Sqrt(5.0)));
			var disc = new (float x, float y, float r)[n];
			for (var k = 0; k < n; k++) {
				var r = clusterRadius * (float)Math.Sqrt((k + 0.5f) / n);
				var a = k * goldenAngle;
				disc[k] = (r * (float)Math.Cos(a), r * (float)Math.Sin(a), r);
			}

			var slotTaken = new bool[n];
			var assignment = new Dictionary<int, int>(); // vertexId → disc slot

			// Reserve contiguous slots for each same-block group (≥2 members)
			if (blockGroups != null)
				AssignBlockGroupSlots(cluster, disc, slotTaken, assignment, vertexAngle, blockGroups, n);

			// Assign remaining vertices: isolated → inner slots, externally-connected → outer slots
			var freeSlots = Enumerable.Range(0, n).Where(k => !slotTaken[k]).ToList();
			var unassigned = cluster.Where(v => !assignment.ContainsKey(v)).ToList();

			var noExternal = unassigned.Where(v => vertexStrength[v] == 0).ToList();
			var hasExternal = unassigned.Where(v => vertexStrength[v] > 0).OrderBy(v => vertexAngle[v]).ToList();

			var innerFree = freeSlots.OrderBy(k => disc[k].r).ToList();
			var innerSlots = innerFree.Take(noExternal.Count).ToList();
			var outerSlots = innerFree.Skip(noExternal.Count)
				.OrderBy(k => (float)Math.Atan2(disc[k].y, disc[k].x))
				.ToList();

			for (var i = 0; i < noExternal.Count; i++)
				assignment[noExternal[i]] = innerSlots[i];
			for (var i = 0; i < hasExternal.Count && i < outerSlots.Count; i++)
				assignment[hasExternal[i]] = outerSlots[i];

			foreach (var (vertexId, slot) in assignment) {
				_vertexPositions[vertexId] = new PointF(
					clusterCenterX + disc[slot].x - _vertexRadius,
					clusterCenterY + disc[slot].y - _vertexRadius);
			}
		}

		/// <summary>
		/// Reserves spatially contiguous disc slots for each multi-vertex block group.
		/// </summary>
		static void AssignBlockGroupSlots(
			List<int> cluster,
			(float x, float y, float r)[] disc,
			bool[] slotTaken,
			Dictionary<int, int> assignment,
			Dictionary<int, float> vertexAngle,
			Dictionary<int, int> blockGroups,
			int n)
		{
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

			foreach (var (_, members) in blockMembers) {
				if (members.Count < 2)
					continue;

				// Anchor slot: free slot whose direction best matches the group's average angle
				var ax = members.Average(v => (double)Math.Cos(vertexAngle[v]));
				var ay = members.Average(v => (double)Math.Sin(vertexAngle[v]));
				var groupAngle = (float)Math.Atan2(ay, ax);

				var anchorSlot = -1;
				var bestDot = float.MinValue;
				for (var k = 0; k < n; k++) {
					if (slotTaken[k])
						continue;
					var slotAngle = (float)Math.Atan2(disc[k].y, disc[k].x);
					var dot = (float)(Math.Cos(groupAngle) * Math.Cos(slotAngle)
									+ Math.Sin(groupAngle) * Math.Sin(slotAngle));
					if (dot > bestDot) { bestDot = dot; anchorSlot = k; }
				}
				if (anchorSlot < 0)
					continue;

				var anchorX = disc[anchorSlot].x;
				var anchorY = disc[anchorSlot].y;
				var nearest = Enumerable.Range(0, n)
					.Where(k => !slotTaken[k])
					.OrderBy(k => {
						var dx = disc[k].x - anchorX;
						var dy = disc[k].y - anchorY;
						return dx * dx + dy * dy;
					})
					.Take(members.Count)
					.ToList();

				var sortedMembers = members.OrderBy(v => vertexAngle[v]).ToList();
				var sortedSlots = nearest.OrderBy(k => (float)Math.Atan2(disc[k].y, disc[k].x)).ToList();
				for (var i = 0; i < sortedMembers.Count; i++) {
					assignment[sortedMembers[i]] = sortedSlots[i];
					slotTaken[sortedSlots[i]] = true;
				}
			}
		}

		/// <summary>
		/// Iteratively pushes overlapping vertices within each cluster apart using their actual
		/// rendered radii (which are larger than the base radius for weighted/merged nodes).
		/// </summary>
		void SeparateOverlappingVertices()
		{
			foreach (var cluster in _clusters) {
				var vlist = cluster.Where(_vertexPositions.ContainsKey).ToList();
				if (vlist.Count < 2)
					continue;

				for (var iter = 0; iter < 300; iter++) {
					var moved = false;
					for (var a = 0; a < vlist.Count; a++) {
						for (var b = a + 1; b < vlist.Count; b++) {
							var va = vlist[a];
							var vb = vlist[b];
							var wA = _nodeWeights != null && _nodeWeights.TryGetValue(va, out var wwa) ? wwa : 1;
							var wB = _nodeWeights != null && _nodeWeights.TryGetValue(vb, out var wwb) ? wwb : 1;
							var rA = wA > 1 ? _vertexRadius * (float)Math.Sqrt(wA) : _vertexRadius;
							var rB = wB > 1 ? _vertexRadius * (float)Math.Sqrt(wB) : _vertexRadius;

							var posA = _vertexPositions[va];
							var posB = _vertexPositions[vb];
							var dx = posB.X + rB - (posA.X + rA);
							var dy = posB.Y + rB - (posA.Y + rA);
							var dist = (float)Math.Sqrt(dx * dx + dy * dy);
							var minDist = rA + rB + 2f;
							if (dist >= minDist)
								continue;

							moved = true;
							if (dist < 0.001f) { dx = 1f; dy = 0f; dist = 1f; }
							var push = (minDist - dist) * 0.5f / dist;
							_vertexPositions[va] = new PointF(posA.X - dx * push, posA.Y - dy * push);
							_vertexPositions[vb] = new PointF(posB.X + dx * push, posB.Y + dy * push);
						}
					}
					if (!moved)
						break;
				}
			}
		}

		const float Padding = 30f;
		List<List<int>> _clusters;
		float _vertexRadius;
		readonly Dictionary<int, PointF> _vertexPositions = [];
		Dictionary<int, int> _nodeWeights;
	}
}
