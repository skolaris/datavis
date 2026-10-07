using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeltaVis
{
	/// <summary>
	/// Generates distinct colors for graph clusters using HSL to RGB conversion.
	/// </summary>
	internal class ColorGenerator
	{
		private readonly Dictionary<int, Color> clusterColors = [];

		/// <summary>
		/// Generate distinct colors for each cluster.
		/// Distributes hues evenly around the color wheel.
		/// </summary>
		public void GenerateClusterColors(List<List<int>> clusters)
		{
			clusterColors.Clear();

			var clusterCount = clusters.Count;
			for (var i = 0; i < clusterCount; i++) {
				// Distribute hues evenly around the color wheel
				var hue = (float)i / clusterCount * 360f;
				var clusterColor = HSLToRGB(hue, 0.7f, 0.5f); // High saturation, medium lightness
				clusterColors[i] = clusterColor;
			}
		}

		/// <summary>
		/// Get the color for a specific cluster.
		/// </summary>
		public Color GetClusterColor(int clusterId)
		{
			return clusterColors.TryGetValue(clusterId, out var color) ? color : Color.SteelBlue;
		}

		/// <summary>
		/// Get all cluster colors.
		/// </summary>
		public Dictionary<int, Color> GetAllClusterColors()
		{
			return new Dictionary<int, Color>(clusterColors);
		}

		/// <summary>
		/// Convert HSL (Hue, Saturation, Lightness) to RGB color.
		/// </summary>
		public static Color HSLToRGB(float hue, float saturation, float lightness)
		{
			hue %= 360f;
			saturation = Math.Clamp(saturation, 0f, 1f);
			lightness = Math.Clamp(lightness, 0f, 1f);

			var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
			var hPrime = hue / 60f;
			var x = c * (1 - Math.Abs(hPrime % 2 - 1));

			float r, g, b;
			if (hPrime < 1)
				(r, g, b) = (c, x, 0);
			else if (hPrime < 2)
				(r, g, b) = (x, c, 0);
			else if (hPrime < 3)
				(r, g, b) = (0, c, x);
			else if (hPrime < 4)
				(r, g, b) = (0, x, c);
			else if (hPrime < 5)
				(r, g, b) = (x, 0, c);
			else
				(r, g, b) = (c, 0, x);

			var m = lightness - c / 2;
			r += m;
			g += m;
			b += m;

			return Color.FromArgb(
				Math.Clamp((int)(r * 255), 0, 255),
				Math.Clamp((int)(g * 255), 0, 255),
				Math.Clamp((int)(b * 255), 0, 255)
			);
		}
	}
}
