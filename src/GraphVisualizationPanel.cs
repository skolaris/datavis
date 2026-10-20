using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeltaVis
{
	// Extension method for getting enumerable with indices
	public static class EnumerableExtensions
	{
		public static IEnumerable<(int Index, T Item)> WithIndex<T>(this IEnumerable<T> source)
		{
			return source.Select((item, index) => (index, item));
		}
	}

	public class GraphVisualizationPanel : Panel
	{
		private Dictionary<int, HashSet<int>> graphData;
		private Dictionary<int, PointF> vertexPositions = [];
		private Dictionary<int, int> vertexToCluster = [];
		private List<List<int>> clusters = [];
		private Dictionary<int, Color> clusterColors = [];
		private int selectedVertexId = -1;
		[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
		public bool ShowEdges { get; set; } = true;
		[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
		public bool ColorByModule { get; set; } = false;
		private readonly float vertexRadius = 10f;
		private int maxEdgeCount = 1;

		// Module colours — populated when SetLessonTooltips is called
		private Dictionary<int, Color> _moduleColors;  // vertexId -> colour

		// Phase 1 cache
		private Dictionary<int, HashSet<int>> _clusterCacheKey;
		private List<List<int>> _cachedClusters;
		private Dictionary<int, int> _cachedVertexToCluster;
		private Dictionary<int, Color> _cachedClusterColors;

		// lessonId → blockId, set when org data is loaded
		private Dictionary<int, int> _blockGroups;

		// Merged graph (same-block identical-edge vertices collapsed into one representative)
		private Dictionary<int, HashSet<int>> _mergedGraph;
		private Dictionary<int, int> _nodeWeight;            // representativeId → lesson count
		private Dictionary<int, List<int>> _mergedToOriginals; // representativeId → all lesson ids

		// Phase 2 cancellation — discards stale layout tasks on resize
		private CancellationTokenSource _layoutCts;

		private bool _isBusy;

		private readonly GraphRenderer renderer;
		private Dictionary<int, LessonInfo> _lessonTooltips;
		private readonly ToolTip _toolTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };
		private int _lastTooltipVertex = -1;

		public event EventHandler<VertexSelectedEventArgs> VertexSelected;

		public GraphVisualizationPanel()
		{
			DoubleBuffered = true;
			BackColor = Color.White;
			MouseClick += GraphVisualizationPanel_MouseClick;
			MouseMove += GraphVisualizationPanel_MouseMove;
			Resize += GraphVisualizationPanel_Resize;
			renderer = new GraphRenderer();
		}

		public void SetGraphData(Dictionary<int, HashSet<int>> data)
		{
			graphData = data;
			selectedVertexId = -1;

			if (graphData != null && graphData.Count > 0) {
				BuildMergedGraph();

				var activeGraph = _mergedGraph ?? graphData;
				maxEdgeCount = activeGraph.Values.Max(edges => edges.Count);
				if (maxEdgeCount == 0)
					maxEdgeCount = 1;

				// Cancel any running layout — we're about to rebuild from scratch
				_layoutCts?.Cancel();

				DetectAndLayoutAsync();
			}
		}

		public void HighlightVertex(int vertexId)
		{
			selectedVertexId = vertexId;
			Invalidate();
		}

		public void ClearHighlight()
		{
			selectedVertexId = -1;
			Invalidate();
		}

		public void SetLessonTooltips(Dictionary<int, LessonInfo> tooltips)
		{
			_lessonTooltips = tooltips;
			BuildModuleColors(tooltips);

			// Rebuild block groups and invalidate the cluster cache so DetectClusters
			// re-runs with the new block information.
			_blockGroups = tooltips?.Count > 0
				? tooltips.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BlockId)
				: null;
			_clusterCacheKey = null;

			if (graphData != null && graphData.Count > 0) {
				BuildMergedGraph();
				var activeGraph = _mergedGraph ?? graphData;
				maxEdgeCount = activeGraph.Values.Max(edges => edges.Count);
				if (maxEdgeCount == 0)
					maxEdgeCount = 1;
				DetectAndLayoutAsync();
			}
			else
				Invalidate();
		}

		private void BuildModuleColors(Dictionary<int, LessonInfo> tooltips)
		{
			if (tooltips == null || tooltips.Count == 0) {
				_moduleColors = null;
				return;
			}

			// Collect distinct module codes and assign evenly-spaced hues
			var moduleCodes = tooltips.Values
				.Select(i => i.Module ?? string.Empty)
				.Distinct()
				.OrderBy(m => m)
				.ToList();

			var moduleHue = new Dictionary<string, Color>(moduleCodes.Count);
			for (var i = 0; i < moduleCodes.Count; i++) {
				var hue = moduleCodes.Count > 1 ? (float)i / moduleCodes.Count * 360f : 200f;
				moduleHue[moduleCodes[i]] = ColorGenerator.HSLToRGB(hue, 0.65f, 0.55f);
			}

			_moduleColors = new Dictionary<int, Color>(tooltips.Count);
			foreach (var kvp in tooltips)
				_moduleColors[kvp.Key] = moduleHue.TryGetValue(kvp.Value.Module ?? string.Empty, out var c) ? c : Color.SteelBlue;
		}

		/// <summary>
		/// Phase 1 + Phase 2: run cluster detection (if cache is stale) then layout.
		/// Called when graph data changes.
		/// </summary>
		private async void DetectAndLayoutAsync()
		{
			var capturedGraph = _mergedGraph ?? graphData;

			_isBusy = true;
			Invalidate();

			// Phase 1 — only when graphData reference has changed
			var capturedBlockGroups = _blockGroups;

			if (!ReferenceEquals(_clusterCacheKey, capturedGraph)) {
				try {
					var (newClusters, newVertexToCluster, newClusterColors) = await Task.Run(() => {
						var detector = new ClusterDetector();
						var colorGen = new ColorGenerator();
						var localClusters = new List<List<int>>();
						var localVertexToCluster = new Dictionary<int, int>();

						ClusterDetector.DetectClusters(capturedGraph, localClusters, localVertexToCluster, capturedBlockGroups);
						colorGen.GenerateClusterColors(localClusters);
						var localColors = colorGen.GetAllClusterColors();

						return (localClusters, localVertexToCluster, localColors);
					});

					// Swap cache on the UI thread
					_clusterCacheKey = capturedGraph;
					_cachedClusters = newClusters;
					_cachedVertexToCluster = newVertexToCluster;
					_cachedClusterColors = newClusterColors;
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					_isBusy = false;
					Invalidate();
					return;
				}
			}

			// Phase 2 — layout using (now-fresh) cached cluster data
			await RunLayoutAsync(capturedGraph);
		}

		/// <summary>
		/// Phase 2 only: recompute vertex positions using the cached cluster data.
		/// Called on resize (cluster data is unchanged).
		/// </summary>
		private async void CalculateLayoutAsync()
		{
			// Nothing to do if the cluster cache hasn't been built yet
			if (_cachedClusters == null || graphData == null)
				return;

			await RunLayoutAsync(_mergedGraph ?? graphData);
		}

		private async Task RunLayoutAsync(Dictionary<int, HashSet<int>> capturedGraph)
		{
			// Cancel any layout already in flight
			_layoutCts?.Cancel();
			_layoutCts = new CancellationTokenSource();
			var token = _layoutCts.Token;

			_isBusy = true;
			Invalidate();

			// Snapshot panel dimensions and cluster cache for the background thread
			var capturedClusters = _cachedClusters;
			var capturedVertexToCluster = _cachedVertexToCluster;
			var capturedColors = _cachedClusterColors;
			var capturedBlockGroups = _blockGroups;
			var capturedNodeWeight = _nodeWeight;
			var capturedWidth = Width;
			var capturedHeight = Height;
			var capturedRadius = vertexRadius;

			Dictionary<int, PointF> newPositions;

			try {
				newPositions = await Task.Run(() => {
					var layout = new GraphLayoutCalculator();
					var localPositions = layout.CalculateLayout(
						capturedGraph,
						capturedClusters,
						capturedVertexToCluster,
						capturedRadius,
						capturedWidth,
						capturedHeight,
						capturedBlockGroups,
						capturedNodeWeight);

					token.ThrowIfCancellationRequested();
					return localPositions;
				}, token);
			}
			catch (OperationCanceledException) {
				// A newer resize arrived — the newer task will update the UI
				return;
			}

			// Back on UI thread: publish results
			vertexPositions = newPositions;
			clusters = capturedClusters;
			vertexToCluster = capturedVertexToCluster;
			clusterColors = capturedColors;
			_isBusy = false;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (_isBusy) {
				using var font = new Font("Arial", 12);
				e.Graphics.DrawString("Calculating layout\u2026", font, Brushes.Gray, 20, 20);
				return;
			}

			renderer.SetVertexRadius(vertexRadius);
			renderer.SetMaxEdgeCount(GraphRenderer.ComputeMaxWeightedScore(_mergedGraph ?? graphData, vertexToCluster));

			var origLessonCount = graphData?.Count ?? 0;
			var origTotalEdges  = graphData?.Sum(x => x.Value.Count) ?? 0;
			var origSelectedEdges = selectedVertexId >= 0 && graphData != null && graphData.TryGetValue(selectedVertexId, out var selEdges)
				? selEdges.Count
				: -1;
			// For a merged representative, sum the original edge counts of all represented lessons
			if (origSelectedEdges < 0 && selectedVertexId >= 0 &&
				_mergedToOriginals != null && _mergedToOriginals.TryGetValue(selectedVertexId, out var origIds)) {
				origSelectedEdges = origIds
					.Where(id => graphData != null && graphData.ContainsKey(id))
					.Sum(id => graphData[id].Count);
			}

			renderer.Render(
					e,
					_mergedGraph ?? graphData,
					clusters,
					vertexPositions,
					clusterColors,
					vertexToCluster,
					selectedVertexId,
					Width,
					Height,
					ShowEdges,
					ColorByModule ? _moduleColors : null,
					_lessonTooltips,
					_nodeWeight,
					_mergedToOriginals,
					origLessonCount,
					origTotalEdges,
					origSelectedEdges
				);
		}

		private void GraphVisualizationPanel_MouseClick(object sender, MouseEventArgs e)
		{
			if (_isBusy)
				return;

			foreach (var kvp in vertexPositions) {
				var vertexId = kvp.Key;
				var pos = kvp.Value;
				var r = GetActualRadius(vertexId);
				var rect = new RectangleF(pos.X, pos.Y, r * 2, r * 2);

				if (rect.Contains(e.Location)) {
					selectedVertexId = vertexId;
					var representedIds = _mergedToOriginals != null && _mergedToOriginals.TryGetValue(vertexId, out var merged)
						? merged
						: [vertexId];
					VertexSelected?.Invoke(this, new VertexSelectedEventArgs(vertexId, representedIds));
					Invalidate();
					return;
				}
			}

			selectedVertexId = -1;
			Invalidate();
		}

		private void GraphVisualizationPanel_MouseMove(object sender, MouseEventArgs e)
		{
			if (_isBusy || _lessonTooltips == null)
				return;

			foreach (var kvp in vertexPositions) {
				var r = GetActualRadius(kvp.Key);
				var rect = new RectangleF(kvp.Value.X, kvp.Value.Y, r * 2, r * 2);
				if (rect.Contains(e.Location)) {
					if (_lastTooltipVertex == kvp.Key)
						return;

					_lastTooltipVertex = kvp.Key;

					// Expand merged node to all represented lessons
					var represented = _mergedToOriginals != null && _mergedToOriginals.TryGetValue(kvp.Key, out var originals)
						? originals
						: [kvp.Key];

					var lines = new System.Text.StringBuilder();
					foreach (var lid in represented) {
						lines.Append($"Lesson {lid}");
						if (_lessonTooltips.TryGetValue(lid, out var info)) {
							var detail = info.ToDetailString();
							if (!string.IsNullOrEmpty(detail))
								lines.Append(": ").Append(detail.Replace("   |   ", ", "));
						}
						lines.AppendLine();
					}
					_toolTip.SetToolTip(this, lines.ToString().TrimEnd());
					return;
				}
			}

			// Mouse is not over any vertex
			if (_lastTooltipVertex != -1) {
				_lastTooltipVertex = -1;
				_toolTip.SetToolTip(this, string.Empty);
			}
		}

		private void GraphVisualizationPanel_Resize(object sender, EventArgs e)
		{
			// Skip Phase 1 — reuse cached clusters, only redo layout geometry
			CalculateLayoutAsync();
		}

		/// <summary>
		/// Returns the actual drawn radius for a vertex, scaled by sqrt of its node weight.
		/// </summary>
		private float GetActualRadius(int vertexId)
		{
			if (_nodeWeight != null && _nodeWeight.TryGetValue(vertexId, out var w) && w > 1)
				return vertexRadius * (float)Math.Sqrt(w);
			return vertexRadius;
		}

		/// <summary>
		/// Collapses same-block vertices whose edge sets are identical into a single
		/// representative node. Populates _mergedGraph, _nodeWeight, _mergedToOriginals.
		/// When no merging applies the fields are set to null (callers fall back to graphData).
		/// </summary>
		private void BuildMergedGraph()
		{
			_mergedGraph = null;
			_nodeWeight = null;
			_mergedToOriginals = null;

			if (graphData == null || _blockGroups == null)
				return;

			// Group lesson ids by block
			var byBlock = new Dictionary<int, List<int>>();
			foreach (var kvp in _blockGroups) {
				if (!graphData.ContainsKey(kvp.Key))
					continue;
				if (!byBlock.TryGetValue(kvp.Value, out var list)) { list = []; byBlock[kvp.Value] = list; }
				list.Add(kvp.Key);
			}

			// Find groups where ≥2 lessons share identical edge sets (normalised as sorted string key)
			// Map: original lesson id -> representative id
			var repOf = new Dictionary<int, int>();
			var mergedToOriginals = new Dictionary<int, List<int>>();

			foreach (var (blockId, members) in byBlock) {
				if (members.Count < 2)
					continue;

				var memberSet = members.ToHashSet();

				// Sub-group by edge signature, ignoring intra-block edges so that lessons
				// that only differ by referencing each other still count as identical.
				var bySig = new Dictionary<string, List<int>>();
				foreach (var lid in members) {
					var sig = string.Join(",", graphData[lid].Where(nb => !memberSet.Contains(nb)).OrderBy(x => x));
					if (!bySig.TryGetValue(sig, out var g)) { g = []; bySig[sig] = g; }
					g.Add(lid);
				}

				foreach (var group in bySig.Values) {
					if (group.Count < 2)
						continue;
					var rep = group.Min(); // lowest id becomes the representative
					mergedToOriginals[rep] = [.. group.OrderBy(x => x)];
					foreach (var lid in group)
						repOf[lid] = rep;
				}
			}

			if (mergedToOriginals.Count == 0)
				return; // nothing to merge

			// Build merged graph: replace every non-representative with its representative
			var merged = new Dictionary<int, HashSet<int>>();
			foreach (var kvp in graphData) {
				var lid = kvp.Key;
				var rep = repOf.TryGetValue(lid, out var r) ? r : lid;
				if (rep != lid)
					continue; // skip non-representatives

				if (!merged.TryGetValue(rep, out var edges)) { edges = []; merged[rep] = edges; }
				// Remap all neighbour ids through repOf as well
				foreach (var nb in kvp.Value) {
					var nbRep = repOf.TryGetValue(nb, out var nr) ? nr : nb;
					if (nbRep != rep)
						edges.Add(nbRep);
				}
			}
			// Include non-merged singletons
			foreach (var kvp in graphData) {
				if (repOf.TryGetValue(kvp.Key, out var r) && r != kvp.Key)
					continue;
				if (merged.ContainsKey(kvp.Key))
					continue;
				var edges = new HashSet<int>();
				foreach (var nb in kvp.Value) {
					var nbRep = repOf.TryGetValue(nb, out var nr) ? nr : nb;
					if (nbRep != kvp.Key)
						edges.Add(nbRep);
				}
				merged[kvp.Key] = edges;
			}

			// Build nodeWeight
			var nodeWeight = new Dictionary<int, int>();
			foreach (var kvp in mergedToOriginals)
				nodeWeight[kvp.Key] = kvp.Value.Count;

			_mergedGraph = merged;
			_nodeWeight = nodeWeight;
			_mergedToOriginals = mergedToOriginals;
		}
	}
}
