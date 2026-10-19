using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeltaVis
{
	/// <summary>
	/// Detects communities in a graph using greedy modularity-based merging.
	/// </summary>
	internal class ClusterDetector
	{
		/// <summary>
		/// Detect clusters in the graph using greedy community detection.
		/// Returns clusters list and vertex-to-cluster mapping.
		/// When <paramref name="blockGroups"/> is provided, lessons sharing a block are
		/// kept together as a priority sub-cluster within the same community.
		/// </summary>
		public static void DetectClusters(
			Dictionary<int, HashSet<int>> graphData,
			List<List<int>> clusters,
			Dictionary<int, int> vertexToCluster,
			Dictionary<int, int> blockGroups = null)
		{
			clusters.Clear();
			vertexToCluster.Clear();

			if (graphData == null || graphData.Count == 0)
				return;

			var allVertices = graphData.Keys.OrderBy(x => x).ToList();
			var vertexCount = allVertices.Count;

			// Build undirected adjacency
			Dictionary<int, HashSet<int>> undirectedGraph = [];
			foreach (var vertex in allVertices) {
				undirectedGraph[vertex] = [];
			}

			// Add edges in both directions
			foreach (var kvp in graphData) {
				foreach (var target in kvp.Value) {
					undirectedGraph[kvp.Key].Add(target);
					if (undirectedGraph.TryGetValue(target, out var neighbors)) {
						neighbors.Add(kvp.Key);
					}
				}
			}

			// Start: each vertex is its own community
			Dictionary<int, HashSet<int>> communities = [];
			Dictionary<int, int> vertexToCommunity = [];

			for (var i = 0; i < allVertices.Count; i++) {
				communities[i] = [allVertices[i]];
				vertexToCommunity[allVertices[i]] = i;
			}

			// Greedy merge: repeatedly merge communities with best modularity gain
			// Target: 5-15 communities for good visualization
			var targetCommunities = Math.Max(5, Math.Min(15, vertexCount / 50));

			while (communities.Count > targetCommunities) {
				var communityList = communities.Keys.ToList();
				var n = communityList.Count;

				// Pre-allocate one slot per (i,j) pair in row-major order so that
				// each Parallel.For thread writes to a unique index — no locks needed.
				// flatIdx(i,j) = i*(n-1) - i*(i-1)/2 + (j-i-1)
				var totalPairs = n * (n - 1) / 2;
				var pairScores = new float[totalPairs];
				var pairCom1 = new int[totalPairs];
				var pairCom2 = new int[totalPairs];

				Parallel.For(0, n, i => {
					for (var j = i + 1; j < n; j++) {
						int c1 = communityList[i], c2 = communityList[j];
						var edgesBetween = 0;
						foreach (var v1 in communities[c1])
							foreach (var v2 in communities[c2])
								if (undirectedGraph[v1].Contains(v2))
									edgesBetween++;

						var flatIdx = i * (n - 1) - i * (i - 1) / 2 + (j - i - 1);
						pairCom1[flatIdx] = c1;
						pairCom2[flatIdx] = c2;
						pairScores[flatIdx] = edgesBetween > 0
							? (float)edgesBetween / (communities[c1].Count * communities[c2].Count)
							: -1f;
					}
				});

				// Sequential scan in the same row-major order as the original nested loop
				// so tie-breaking is identical to the sequential version.
				int bestCom1 = -1, bestCom2 = -1;
				var bestGain = -1f;
				for (var k = 0; k < totalPairs; k++) {
					if (pairScores[k] > bestGain) {
						bestGain = pairScores[k];
						bestCom1 = pairCom1[k];
						bestCom2 = pairCom2[k];
					}
				}

				if (bestCom1 < 0 || bestGain < 0.001f)
					break;

				// Merge bestCom2 into bestCom1
				communities[bestCom1].UnionWith(communities[bestCom2]);
				foreach (var vertex in communities[bestCom2])
					vertexToCommunity[vertex] = bestCom1;
				communities.Remove(bestCom2);
			}

			// Force-merge communities that share a block so all lessons in a block
			// end up in the same community regardless of the greedy result.
			if (blockGroups != null && blockGroups.Count > 0) {
				var blockToVertices = new Dictionary<int, List<int>>();
				foreach (var kvp in blockGroups) {
					if (!vertexToCommunity.ContainsKey(kvp.Key))
						continue;
					if (!blockToVertices.TryGetValue(kvp.Value, out var list)) {
						list = [];
						blockToVertices[kvp.Value] = list;
					}
					list.Add(kvp.Key);
				}

				foreach (var blockVertices in blockToVertices.Values) {
					if (blockVertices.Count < 2)
						continue;

					var targetCom = vertexToCommunity[blockVertices[0]];
					for (var bi = 1; bi < blockVertices.Count; bi++) {
						var srcCom = vertexToCommunity[blockVertices[bi]];
						if (srcCom == targetCom)
							continue;

						// Follow the chain in case either community was merged away
						if (!communities.ContainsKey(targetCom))
							targetCom = vertexToCommunity[blockVertices[0]];
						if (!communities.ContainsKey(srcCom))
							srcCom = vertexToCommunity[blockVertices[bi]];
						if (srcCom == targetCom)
							continue;

						communities[targetCom].UnionWith(communities[srcCom]);
						foreach (var v in communities[srcCom])
							vertexToCommunity[v] = targetCom;
						communities.Remove(srcCom);
					}
				}
			}

			// Convert to clusters format
			Dictionary<int, int> communityRemap = [];
			var newId = 0;
			foreach (var kvp in communities) {
				communityRemap[kvp.Key] = newId;
				clusters.Add(kvp.Value.ToList());
				newId++;
			}

			// Populate vertexToCluster
			foreach (var kvp in vertexToCommunity) {
				var vertex = kvp.Key;
				var com = kvp.Value;
				var remappedId = communityRemap.TryGetValue(com, out var id) ? id : 0;
				vertexToCluster[vertex] = remappedId;
			}
		}
	}
}
