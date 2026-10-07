using System;

namespace DeltaVis
{
	public class VertexSelectedEventArgs(int vertexId) : EventArgs
	{
		public int VertexId { get; } = vertexId;
	}
}
