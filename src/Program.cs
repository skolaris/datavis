using System;
using System.Windows.Forms;

namespace DeltaVis
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			ApplicationConfiguration.Initialize();
			Application.Run(new Form1());
		}
	}
}
