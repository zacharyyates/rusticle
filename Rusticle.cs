using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace aim {
	public partial class Reticle : Form {

		public const int WM_NCLBUTTONDOWN = 0xA1;
		public const int HT_CAPTION = 0x2;

		[DllImportAttribute("user32.dll")]
		public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
		[DllImportAttribute("user32.dll")]
		public static extern bool ReleaseCapture();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool GetWindowRect(HandleRef hWnd, out RECT lpRect);

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT {
			public int Left;        // x position of upper-left corner
			public int Top;         // y position of upper-left corner
			public int Right;       // x position of lower-right corner
			public int Bottom;      // y position of lower-right corner
		}

		Timer _refreshTimer = new Timer();

		public Reticle() {
			InitializeComponent();

			Opacity = 0.55;
			TransparencyKey = Color.White;
			BackColor = Color.White;

			_refreshTimer.Interval = 1000;
			_refreshTimer.Tick += RefreshTimer_Tick;
		}

		void RefreshTimer_Tick(object sender, EventArgs e) {
			AutoAlign();
		}

		void AutoAlign() {
			SuspendLayout();

			var handle = GetRustHandle();
			if (handle == IntPtr.Zero)
				return;

			RECT rust;
			if (!GetWindowRect(new HandleRef(this, handle), out rust))
				return;

			var rustWidth = rust.Right - rust.Left;
			var rustHeight = rust.Bottom - rust.Top;
			
			var offsetX = Width / 2;

			var left = rust.Left - offsetX + (rustWidth / 2) + 1;
			var top = rust.Top + (rustHeight / 2) + 6;

			Location = new Point(left, top);

			ResumeLayout();
		}

		IntPtr GetRustHandle() {
			var processes = Process.GetProcessesByName("rust");
			if (processes.Length > 0) {
				return processes[0].MainWindowHandle;
			}

			return IntPtr.Zero;
		}

		void Reticle_MouseDown(object sender, MouseEventArgs e) {
			if (e.Button == MouseButtons.Left) {
				ReleaseCapture();
				SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
			}
		}

		void Reticle_MouseDoubleClick(object sender, MouseEventArgs e) {
			Close();
		}

		void Reticle_KeyDown(object sender, KeyEventArgs e) {
			if (e.KeyCode == Keys.Home) {
				AutoAlign();
			}
		}

		void Reticle_Load(object sender, EventArgs e) {
			AutoAlign();
			_refreshTimer.Start();
		}

		void Reticle_FormClosed(object sender, FormClosedEventArgs e) {
			_refreshTimer.Tick -= RefreshTimer_Tick;
			_refreshTimer.Stop();
			_refreshTimer.Dispose();
		}
	}
}