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
		public Dictionary<int, PointF> CalculateLayout(
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, int> vertexToCluster,
			float vertexRadius,
			int panelWidth,
			int panelHeight,
			Dictionary<int, int> blockGroups = null,
			Dictionary<int, int> nodeWeights = null)
		{
			_vertexPositions.Clear();
			if (graphData == null || graphData.Count == 0)
				return _vertexPositions;

			_clusters = clusters;
			_vertexRadius = vertexRadius;
			_nodeWeights = nodeWeights;

			var centerX = panelWidth / 2f;
			var centerY = panelHeight / 2f;
			var interClusterEdges = BuildInterClusterEdges(graphData, vertexToCluster);
			var gap = vertexRadius * 2f + ClusterBoundaryPadding * 2f;
			PlaceClusters(centerX, centerY, gap,
				out var clusterRadii, out var clusterCX, out var clusterCY);
			// Vertex circles extend vertexRadius beyond clusterRadius; cluster boundary
			// rectangles add a further ClusterBoundaryPadding — subtract both from the
			// available area so neither overflows the panel edge.
			var renderMargin = vertexRadius + ClusterBoundaryPadding;
			ScaleAndCenterLayout(clusterCX, clusterCY, clusterRadii, centerX, centerY,
				panelWidth - 2 * (Padding + renderMargin), panelHeight - 2 * (Padding + renderMargin));

			for (var i = 0; i < _clusters.Count; i++) {
				PositionClusterVertices(clusters[i], clusterCX[i], clusterCY[i], clusterRadii[i],
					interClusterEdges, clusterCX, clusterCY, blockGroups);
			}
			SeparateOverlappingVertices();
			ClampVerticesToBounds(panelWidth, panelHeight);
			return _vertexPositions;
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
		/// Computes cluster radii then places cluster centres using greedy circle packing:
		/// clusters are sorted largest-first; each is placed at the position, on the contact
		/// ring of any already-placed cluster, that minimises distance to the panel centre
		/// with a <see cref="SpreadBias"/>-weighted tiebreaker that favours spreading away
		/// from neighbours.  A final separation pass resolves floating-point residuals.
		/// </summary>
		void PlaceClusters(
			float targetX, float targetY, float gap,
			out float[] clusterRadii, out float[] clusterCX, out float[] clusterCY)
		{
			var n = _clusters.Count;
			clusterRadii = new float[n];
			clusterCX = new float[n];
			clusterCY = new float[n];

			for (var i = 0; i < n; i++) {
				var totalArea = _clusters[i].Sum(v => {
					var r = GetWeightedRadius(v);
					return r * r;
				});
				clusterRadii[i] = 2.4f * (float)Math.Sqrt(totalArea / Math.PI);
			}

			if (n == 0) return;

			var radii = clusterRadii; //local copy: out params cannot be captured in lambdas (CS1628)
			var order = Enumerable.Range(0, n).OrderByDescending(i => radii[i]).ToArray();
			var placed = new bool[n];

			// Largest cluster at panel centre
			clusterCX[order[0]] = targetX;
			clusterCY[order[0]] = targetY;
			placed[order[0]] = true;

			const int AngleSteps = 360;

			for (var k = 1; k < n; k++) {
				var idx = order[k];
				var rNew = clusterRadii[idx];
				var bestX = targetX;
				var bestY = targetY + clusterRadii[order[0]] + rNew + gap;
				var bestScore = float.MaxValue;

				for (var p = 0; p < n; p++) {
					if (!placed[p]) continue;
					var contactR = clusterRadii[p] + rNew + gap;

					for (var step = 0; step < AngleSteps; step++) {
						var angle = step * (2f * (float)Math.PI / AngleSteps);
						var cx = clusterCX[p] + contactR * (float)Math.Cos(angle);
						var cy = clusterCY[p] + contactR * (float)Math.Sin(angle);

						var minDistToOthers = float.MaxValue;
						var valid = true;
						for (var q = 0; q < n; q++) {
							if (!placed[q]) continue;
							var dx = cx - clusterCX[q];
							var dy = cy - clusterCY[q];
							var d2 = dx * dx + dy * dy;
							var minD = clusterRadii[q] + rNew + gap;
							if (d2 < minD * minD - 1f) { valid = false; break; }
							var d = (float)Math.Sqrt(d2);
							if (d < minDistToOthers) minDistToOthers = d;
						}

						if (!valid) continue;

						var distToTarget = (float)Math.Sqrt((cx - targetX) * (cx - targetX) + (cy - targetY) * (cy - targetY));
						var score = distToTarget - minDistToOthers * SpreadBias;
						if (score < bestScore) {
							bestScore = score;
							bestX = cx;
							bestY = cy;
						}
					}
				}

				clusterCX[idx] = bestX;
				clusterCY[idx] = bestY;
				placed[idx] = true;
			}

			SeparateClusters(clusterCX, clusterCY, clusterRadii, gap);
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
		/// Scales and re-centres the cluster layout to fill the available panel area.
		/// Cluster centre positions always scale to fill the space; radii are only reduced
		/// (never grown) so intra-cluster vertex density stays consistent.
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

			var scale = Math.Min(availWidth / (maxX - minX), availHeight / (maxY - minY));
			var radiusScale = Math.Min(scale, 1f); //shrink radii when compressing; keep them when expanding
			var layoutCX = (minX + maxX) / 2f;
			var layoutCY = (minY + maxY) / 2f;
			for (var i = 0; i < n; i++) {
				clusterCX[i] = centerX + (clusterCX[i] - layoutCX) * scale;
				clusterCY[i] = centerY + (clusterCY[i] - layoutCY) * scale;
				clusterRadii[i] *= radiusScale;
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
			float[] clusterCX, float[] clusterCY,
			Dictionary<int, int> blockGroups)
		{
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
					var dx = clusterCX[kvp.Key] - clusterCenterX;
					var dy = clusterCY[kvp.Key] - clusterCenterY;
					var dist = (float)Math.Sqrt(dx * dx + dy * dy);
					if (dist < 0.001f)
						continue;

					totalX += kvp.Value * dx / dist;
					totalY += kvp.Value * dy / dist;
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
				var hasExternal = unassigned.Where(v => vertexStrength[v] > 0)
					.OrderByDescending(v => vertexStrength[v]).ToList();
				var innerFree = freeSlots.OrderBy(k => disc[k].r).ToList();
				var innerSlots = innerFree.Take(noExternal.Count).ToList();
				var freeOuterSlots = new HashSet<int>(innerFree.Skip(noExternal.Count));

				for (var i = 0; i < noExternal.Count; i++)
					assignment[noExternal[i]] = innerSlots[i];

				// Strongest-first greedy: each vertex claims the free outer slot whose angle
				// most closely faces its weighted target-cluster direction.
				foreach (var v in hasExternal) {
					if (freeOuterSlots.Count == 0) break;
					var targetAngle = vertexAngle[v];
					var bestSlot = freeOuterSlots.MinBy(k => {
						var diff = targetAngle - (float)Math.Atan2(disc[k].y, disc[k].x);
						return Math.Abs((float)Math.IEEERemainder(diff, 2 * Math.PI));
					});
					assignment[v] = bestSlot;
					freeOuterSlots.Remove(bestSlot);
				}

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
				var groupAngle = ComputeAngleFromComponents(
					members.Average(v => (double)Math.Cos(vertexAngle[v])),
					members.Average(v => (double)Math.Sin(vertexAngle[v])));

				var anchorSlot = -1;
				var bestDot = float.MinValue;
				for (var k = 0; k < n; k++) {
					if (slotTaken[k])
						continue;

					var slotAngle = (float)Math.Atan2(disc[k].y, disc[k].x);
					var dot = AngleDotProduct(groupAngle, slotAngle);
					if (dot > bestDot) {
						bestDot = dot;
						anchorSlot = k;
					}
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
							var rA = GetWeightedRadius(va);
							var rB = GetWeightedRadius(vb);

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

		//gets the actual rendered radius for a vertex, accounting for weight
		float GetWeightedRadius(int vertexId)
		{
			if (_nodeWeights != null && _nodeWeights.TryGetValue(vertexId, out var w) && w > 1)
				return _vertexRadius * (float)Math.Sqrt(w);
			return _vertexRadius;
		}

		//computes an angle from a weighted vector sum of cos/sin components
		static float ComputeAngleFromComponents(double sumCos, double sumSin)
			=> (float)Math.Atan2(sumSin, sumCos);

		//dot product of two angles expressed as radians
		static float AngleDotProduct(float a, float b)
			=> (float)(Math.Cos(a) * Math.Cos(b) + Math.Sin(a) * Math.Sin(b));

		//clamps every vertex so none sit outside the panel boundary after overlap separation
		void ClampVerticesToBounds(int panelWidth, int panelHeight)
		{
			foreach (var vertexId in _vertexPositions.Keys.ToList()) {
				var pos = _vertexPositions[vertexId];
				var r = GetWeightedRadius(vertexId);
				var x = Math.Clamp(pos.X, Padding, panelWidth - Padding - r * 2);
				var y = Math.Clamp(pos.Y, Padding, panelHeight - Padding - r * 2);
				if (x != pos.X || y != pos.Y)
					_vertexPositions[vertexId] = new PointF(x, y);
			}
		}

		const float Padding = 30f;
		//must match the padding used in GraphRenderer.DrawClusterBoundaries
		const float ClusterBoundaryPadding = 15f;
		//how much angular spread is favoured vs. proximity to centre when placing each cluster
		const float SpreadBias = 0.5f;
		List<List<int>> _clusters;
		float _vertexRadius;
		readonly Dictionary<int, PointF> _vertexPositions = [];
		Dictionary<int, int> _nodeWeights;
	}
}
