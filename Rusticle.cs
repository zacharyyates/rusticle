using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MovablePython;

namespace Rusticle {
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

		Hotkey _settingsHotKey;
		Hotkey _resetHotKey;
		Hotkey _exitHotKey;
		Hotkey _upHotKey;
		Hotkey _downHotKey;
		Hotkey _leftHotKey;
		Hotkey _rightHotKey;

		bool _inSettingsMode = false;

		public int OffsetX {
			get { return Rusticle.Properties.Settings.Default.OffsetX; }
			set { Rusticle.Properties.Settings.Default.OffsetX = value; }
		}
		public int OffsetY {
			get { return Rusticle.Properties.Settings.Default.OffsetY; }
			set { Rusticle.Properties.Settings.Default.OffsetY = value; }
		}

		public Reticle() {
			InitializeComponent();

			Opacity = 0.55;
			TransparencyKey = Color.White;
			BackColor = Color.White;

			_settingsHotKey = CreateHotKey(Keys.Pause, SettingsHotkey_Pressed);
			_resetHotKey = CreateHotKey(Keys.Home, ResetHotkey_Pressed);
			_exitHotKey = CreateHotKey(Keys.End, ExitHotkey_Pressed);

			_upHotKey = CreateHotKey(Keys.Up, UpHotkey_Pressed);
			_downHotKey = CreateHotKey(Keys.Down, DownHotkey_Pressed);
			_leftHotKey = CreateHotKey(Keys.Left, LeftHotkey_Pressed);
			_rightHotKey = CreateHotKey(Keys.Right, RightHotkey_Pressed);
			
			_refreshTimer.Interval = 1000;
			_refreshTimer.Tick += RefreshTimer_Tick;

			_settingsHotKey.Register(this);
		}

		void Reticle_Load(object sender, EventArgs e) {
			RefreshPosition();
			_refreshTimer.Start();
		}

		void Reticle_FormClosed(object sender, FormClosedEventArgs e) {
			_refreshTimer.Tick -= RefreshTimer_Tick;
			_refreshTimer.Stop();
			_refreshTimer.Dispose();

			UnregisterHotkeys();
			_settingsHotKey.Unregister();
		}

		void RefreshTimer_Tick(object sender, EventArgs e) {
			RefreshPosition();
		}

		void SettingsHotkey_Pressed(object sender, HandledEventArgs e) {
			_inSettingsMode = !_inSettingsMode;

			if (_inSettingsMode)
				RegisterHotkeys();
			else
				UnregisterHotkeys();
		}

		void ResetHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				// best guess for center screen
				OffsetX = 1;
				OffsetY = 6;
				RefreshPosition();
			}
		}

		void ExitHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				Close();
			}
		}

		void UpHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				OffsetY -= 1;
				RefreshPosition();
			}
		}

		void DownHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				OffsetY += 1;
				RefreshPosition();
			}
		}

		void LeftHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				OffsetX -= 1;
				RefreshPosition();
			}
		}

		void RightHotkey_Pressed(object sender, HandledEventArgs e) {
			if (_inSettingsMode) {
				e.Handled = true;
				OffsetX += 1;
				RefreshPosition();
			}
		}

		void Reticle_MouseDown(object sender, MouseEventArgs e) {
			if (e.Button == MouseButtons.Left) {
				ReleaseCapture();
				SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
			}
		}

		void RegisterHotkeys() {
			_resetHotKey.Register(this);
			_exitHotKey.Register(this);
			_upHotKey.Register(this);
			_downHotKey.Register(this);
			_leftHotKey.Register(this);
			_rightHotKey.Register(this);
		}
		void UnregisterHotkeys() {
			_resetHotKey.Unregister();
			_exitHotKey.Unregister();
			_upHotKey.Unregister();
			_downHotKey.Unregister();
			_leftHotKey.Unregister();
			_rightHotKey.Unregister();
		}

		void RefreshPosition() {
			SuspendLayout();

			var handle = GetRustHandle();
			if (handle == IntPtr.Zero) {
				Visible = false;
				return;
			}

			RECT rustWindow;
			if (!GetWindowRect(new HandleRef(this, handle), out rustWindow))
				return;

			var rustWidth = rustWindow.Right - rustWindow.Left;
			var rustHeight = rustWindow.Bottom - rustWindow.Top;

			var offsetX = Width / 2;

			var left = rustWindow.Left - offsetX + (rustWidth / 2) + OffsetX;
			var top = rustWindow.Top + (rustHeight / 2) + OffsetY;

			Location = new Point(left, top);

			Visible = true;
			ResumeLayout();
		}

		IntPtr GetRustHandle() {
			var processes = Process.GetProcessesByName("rust");
			if (processes.Length > 0) {
				return processes[0].MainWindowHandle;
			}

			return IntPtr.Zero;
		}

		Hotkey CreateHotKey(Keys keys, HandledEventHandler handler, bool control = false) {
			var hotkey = new Hotkey {
				KeyCode = keys,
				Control = control
			};
			hotkey.Pressed += handler;
			return hotkey;
		}
	}
}