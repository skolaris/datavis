using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeltaVis
{
	/// <summary>
	/// Parses an organisation JSON file and resolves per-lesson metadata
	/// (module, teaching group, subgroups, class groups, lecturers).
	/// </summary>
	internal static class OrgDataParser
	{
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true
		};

		/// <summary>
		/// Parses the given JSON file and returns a dictionary keyed by lesson ID.
		/// Lookup failures (e.g. a blockId not present in the blocks array) are
		/// handled gracefully — the affected field is left empty.
		/// </summary>
		public static Dictionary<int, LessonInfo> Parse(string filePath)
		{
			var json = File.ReadAllText(filePath);
			var org = JsonSerializer.Deserialize<OrgFile>(json, _jsonOptions);

			if (org == null)
				return [];

			// Build lookup dictionaries for O(1) access
			var blockById = org.Blocks.ToDictionary(b => b.Id);
			var moduleById = org.Modules.ToDictionary(m => m.Id);
			var lecturerById = org.Lecturers.ToDictionary(l => l.Id);
			var tchGroupById = org.TchGroups.ToDictionary(t => t.Id);
			var subGroupById = org.SubGroups.ToDictionary(s => s.Id);
			var classGroupById = org.ClassGroups.ToDictionary(c => c.Id);

			var result = new Dictionary<int, LessonInfo>(org.Lessons.Count);

			foreach (var lesson in org.Lessons) {
				var info = new LessonInfo { LessonId = lesson.Id, BlockId = lesson.BlockId };

				// Resolve lecturers
				var lecturerCodes = lesson.LessonLecturers
					.Where(ll => lecturerById.ContainsKey(ll.LecturerId))
					.Select(ll => lecturerById[ll.LecturerId].Code)
					.ToList();
				info.Lecturers = string.Join(", ", lecturerCodes);

				// Resolve block → module + teaching group
				if (blockById.TryGetValue(lesson.BlockId, out var block)) {
					if (moduleById.TryGetValue(block.ModuleId, out var module))
						info.Module = module.Code;

					if (tchGroupById.TryGetValue(block.TeachingGroupId, out var tchGroup)) {
						info.TeachingGroup = tchGroup.Code;

						// Resolve subgroups and class groups from the teaching group's subgroup refs
						var subGroupCodes = new List<string>();
						var classGroupCodes = new HashSet<string>();

						foreach (var tgs in tchGroup.TchGroupSubGroups) {
							if (subGroupById.TryGetValue(tgs.ClassSubGroupId, out var subGroup)) {
								subGroupCodes.Add(subGroup.Code);

								if (classGroupById.TryGetValue(subGroup.ClassGroupId, out var classGroup))
									classGroupCodes.Add(classGroup.Code);
							}
						}

						info.SubGroups = string.Join(", ", subGroupCodes);
						info.ClassGroups = string.Join(", ", classGroupCodes);
					}
				}

				result[lesson.Id] = info;
			}
			return result;
		}
	}
}
