using System;
using System.Collections.Generic;

namespace DeltaVis
{
	public class VertexSelectedEventArgs : EventArgs
	{
		public int VertexId { get; }
		public List<int> RepresentedLessonIds { get; }

		public VertexSelectedEventArgs(int vertexId, List<int> representedLessonIds = null)
		{
			VertexId = vertexId;
			RepresentedLessonIds = representedLessonIds ?? [vertexId];
		}
	}
}
