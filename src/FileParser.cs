using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DeltaVis
{
	/// <summary>
	/// Handles parsing of the three supported file types:
	/// delta files, positions files, and graph files.
	/// </summary>
	internal static class FileParser
	{
		public static List<LessonData> ParseDeltaFile(string filePath)
		{
			var lessons = new Dictionary<int, LessonData>();
			var lessonHeaderPattern = new Regex(@"Lesson (\d+):");
			var deltaPattern = new Regex(@"delta: (-?\d+).*adapted delta: (-?\d+)");

			using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var reader = new StreamReader(stream)) {
				string line;
				var currentLessonId = -1;

				while ((line = reader.ReadLine()) != null) {
					var lessonMatch = lessonHeaderPattern.Match(line);
					if (lessonMatch.Success) {
						currentLessonId = int.Parse(lessonMatch.Groups[1].Value);
						if (!lessons.ContainsKey(currentLessonId))
							lessons[currentLessonId] = new LessonData { LessonId = currentLessonId };
						continue;
					}

					if (currentLessonId != -1) {
						var deltaMatch = deltaPattern.Match(line);
						if (deltaMatch.Success) {
							var delta = int.Parse(deltaMatch.Groups[1].Value);
							var adaptedDelta = int.Parse(deltaMatch.Groups[2].Value);
							lessons[currentLessonId].DeltaPairs.Add(new DeltaPair { Delta = delta, AdaptedDelta = adaptedDelta });
						}
					}
				}
			}

			return lessons.Values.ToList();
		}

		public static Dictionary<int, (int ExecutedMoves, HashSet<string> Timeslots)> ParsePositionsFile(string filePath)
		{
			var positions = new Dictionary<int, (int ExecutedMoves, HashSet<string> Timeslots)>();

			try {
				using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream);
				string line;
				var currentLessonId = -1;
				var currentExecutedMoves = 0;
				var lessonHeaderPattern = new Regex(@"Lesson\s+(\d+):\s*(\d+)\s+moves?\s+executed:");
				var coordinatePattern = new Regex(@"\(\s*(\d+)\s*,\s*(\d+)\s*\)");

				while ((line = reader.ReadLine()) != null) {
					var lessonMatch = lessonHeaderPattern.Match(line);
					if (lessonMatch.Success) {
						currentLessonId = int.Parse(lessonMatch.Groups[1].Value);
						currentExecutedMoves = int.Parse(lessonMatch.Groups[2].Value);
						positions[currentLessonId] = (currentExecutedMoves, new HashSet<string>());
						continue;
					}

					if (string.IsNullOrWhiteSpace(line))
						continue;

					if (currentLessonId != -1 && line.Contains("->")) {
						var cleaned = Regex.Replace(line, @"[^\d(),->]", "");
						var matches = coordinatePattern.Matches(cleaned);
						foreach (Match match in matches) {
							var coord = $"{match.Groups[1].Value},{match.Groups[2].Value}";
							positions[currentLessonId].Timeslots.Add(coord);
						}
					}
				}
			}
			catch (Exception ex) {
				MessageBox.Show($"Error loading positions file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return [];
			}

			return positions;
		}

		public static Dictionary<int, HashSet<int>> ParseGraphFile(string filePath)
		{
			var graph = new Dictionary<int, HashSet<int>>();

			try {
				using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (var reader = new StreamReader(stream)) {
					string line;
					var currentLessonId = -1;
					var lessonHeaderPattern = new Regex(@"Lesson\s+(\d+):\s*(\d+)\s+edges:");

					while ((line = reader.ReadLine()) != null) {
						var lessonMatch = lessonHeaderPattern.Match(line);
						if (lessonMatch.Success) {
							currentLessonId = int.Parse(lessonMatch.Groups[1].Value);
							if (!graph.ContainsKey(currentLessonId))
								graph[currentLessonId] = [];
							continue;
						}

						if (string.IsNullOrWhiteSpace(line))
							continue;

						if (currentLessonId != -1) {
							var cleaned = line.Replace("\0", "").Replace("\t", "").Trim();
							if (int.TryParse(cleaned, out var edgeLessonId))
								graph[currentLessonId].Add(edgeLessonId);
						}
					}
				}
			}
			catch (Exception ex) {
				MessageBox.Show($"Error loading graph file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return [];
			}

			return graph;
		}
	}
}
