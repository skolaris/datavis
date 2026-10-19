using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeltaVis
{
	// ── Raw JSON models ────────────────────────────────────────────────────────

	internal class OrgBlock
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }

		[JsonPropertyName("teachingGroupId")]
		public int TeachingGroupId { get; set; }

		[JsonPropertyName("moduleId")]
		public int ModuleId { get; set; }
	}

	internal class OrgClassGroup
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }
	}

	internal class OrgLecturer
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }
	}

	internal class OrgLessonLecturer
	{
		[JsonPropertyName("lecturerId")]
		public int LecturerId { get; set; }
	}

	internal class OrgLesson
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("blockId")]
		public int BlockId { get; set; }

		[JsonPropertyName("lessonLecturers")]
		public List<OrgLessonLecturer> LessonLecturers { get; set; } = [];
	}

	internal class OrgModule
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }
	}

	internal class OrgSubGroup
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }

		[JsonPropertyName("classGroupId")]
		public int ClassGroupId { get; set; }
	}

	internal class OrgTchGroupSubGroup
	{
		[JsonPropertyName("classSubGroupId")]
		public int ClassSubGroupId { get; set; }
	}

	internal class OrgTchGroup
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("code")]
		public string Code { get; set; }

		[JsonPropertyName("tchGroupSubGroups")]
		public List<OrgTchGroupSubGroup> TchGroupSubGroups { get; set; } = [];
	}

	internal class OrgFile
	{
		[JsonPropertyName("blocks")]
		public List<OrgBlock> Blocks { get; set; } = [];

		[JsonPropertyName("classgroups")]
		public List<OrgClassGroup> ClassGroups { get; set; } = [];

		[JsonPropertyName("lecturers")]
		public List<OrgLecturer> Lecturers { get; set; } = [];

		[JsonPropertyName("lessons")]
		public List<OrgLesson> Lessons { get; set; } = [];

		[JsonPropertyName("modules")]
		public List<OrgModule> Modules { get; set; } = [];

		[JsonPropertyName("subgroups")]
		public List<OrgSubGroup> SubGroups { get; set; } = [];

		[JsonPropertyName("tchgroups")]
		public List<OrgTchGroup> TchGroups { get; set; } = [];
	}

	// ── Resolved per-lesson metadata ──────────────────────────────────────────

	public class LessonInfo
	{
		public int LessonId { get; set; }
		public int BlockId { get; set; }
		public string Lecturers { get; set; }
		public string Module { get; set; }
		public string TeachingGroup { get; set; }
		public string SubGroups { get; set; }
		public string ClassGroups { get; set; }

		public string ToDetailString()
		{
			var parts = new List<string>();
			if (!string.IsNullOrEmpty(ClassGroups))
				parts.Add($"Class groups: {ClassGroups}");
			if (!string.IsNullOrEmpty(Module))
				parts.Add($"Module: {Module}");
			if (!string.IsNullOrEmpty(Lecturers))
				parts.Add($"Lecturers: {Lecturers}");
			if (!string.IsNullOrEmpty(SubGroups))
				parts.Add($"Subgroups: {SubGroups}");
			if (!string.IsNullOrEmpty(TeachingGroup))
				parts.Add($"Teaching group: {TeachingGroup}");
			return string.Join("  |  ", parts);
		}
	}
}
