using System.Collections.Generic;
using System.Linq;

namespace DeltaVis
{
	/// <summary>
	/// Resolves the dominant "theme" label for a cluster of lesson vertices.
	/// </summary>
	internal static class ClusterThemeResolver
	{
		/// <summary>
		/// Returns a label such as "Aj  (87%)" or "ZIV / NAV  (45%)" for the cluster,
		/// or null when no single value exceeds 33 % of the cluster.
		/// </summary>
		public static string Resolve(List<int> cluster, Dictionary<int, LessonInfo> lessonInfo)
		{
			if (cluster == null || cluster.Count == 0 || lessonInfo == null)
				return null;

			var moduleCount = new Dictionary<string, int>();
			var classGroupCount = new Dictionary<string, int>();
			var lecturerCount = new Dictionary<string, int>();

			foreach (var vertexId in cluster) {
				if (!lessonInfo.TryGetValue(vertexId, out var info))
					continue;

				IncrementSingle(moduleCount, info.Module);
				IncrementTokens(classGroupCount, info.ClassGroups);
				IncrementTokens(lecturerCount, info.Lecturers);
			}

			// Find the global maximum count across all three categories
			var maxCount = 0;
			maxCount = MaxOf(maxCount, moduleCount);
			maxCount = MaxOf(maxCount, classGroupCount);
			maxCount = MaxOf(maxCount, lecturerCount);

			if (maxCount == 0)
				return null;

			var pct = (double)maxCount / cluster.Count * 100.0;
			if (pct <= 33.0)
				return null;

			// Collect all tokens that share the maximum count
			var winners = new List<string>();
			CollectWinners(winners, moduleCount, maxCount);
			CollectWinners(winners, classGroupCount, maxCount);
			CollectWinners(winners, lecturerCount, maxCount);

			return $"{string.Join(" / ", winners)}  ({(int)pct}%)";
		}

		static void IncrementSingle(Dictionary<string, int> counter, string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return;
			var key = value.Trim();
			counter[key] = counter.TryGetValue(key, out var n) ? n + 1 : 1;
		}

		static void IncrementTokens(Dictionary<string, int> counter, string csv)
		{
			if (string.IsNullOrWhiteSpace(csv))
				return;
			foreach (var token in csv.Split(','))
				IncrementSingle(counter, token.Trim());
		}

		static int MaxOf(int current, Dictionary<string, int> counter)
			=> counter.Count == 0 ? current : System.Math.Max(current, counter.Values.Max());

		static void CollectWinners(List<string> winners, Dictionary<string, int> counter, int maxCount)
		{
			foreach (var kvp in counter) {
				if (kvp.Value == maxCount && !winners.Contains(kvp.Key))
					winners.Add(kvp.Key);
			}
		}
	}
}
