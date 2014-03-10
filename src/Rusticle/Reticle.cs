using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MovablePython;
using WindowsInput;
using WindowsInput.Native;

namespace Rusticle {
	public partial class Reticle : Form {

		const int WM_NCLBUTTONDOWN = 0xA1;
		const int HT_CAPTION = 0x2;
		const uint WM_KEYDOWN = 0x100;
		const uint WM_KEYUP = 0x101;
		const uint VK_W = 0x57;
		const uint VK_SHIFT = 0x10;

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

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		Timer _refreshTimer = new Timer();

		Hotkey _settingsHotkey;
		Hotkey _resetHotkey;
		Hotkey _exitHotkey;

		Hotkey _upHotkey;
		Hotkey _downHotkey;
		Hotkey _leftHotkey;
		Hotkey _rightHotkey;

		Hotkey _runOnHotkey;
		Hotkey _runOffHotkey;

		Process _rustProcess = null;
		IntPtr _rustHandle = IntPtr.Zero;

		bool _inSettingsMode = false;

		object _lockRunMode = new object();
		bool InRunMode {
			get {
				lock (_lockRunMode) { return _inRunMode; }
			}
			set {
				lock (_lockRunMode) { _inRunMode = value; }
			}
		}
		bool _inRunMode = false;

		int OffsetX {
			get { return Rusticle.Properties.Settings.Default.OffsetX; }
			set { Rusticle.Properties.Settings.Default.OffsetX = value; }
		}
		int OffsetY {
			get { return Rusticle.Properties.Settings.Default.OffsetY; }
			set { Rusticle.Properties.Settings.Default.OffsetY = value; }
		}

		IKeyboardSimulator _keyboard;

		public Reticle() {
			InitializeComponent();

			Opacity = 0.55;
			TransparencyKey = Color.White;
			BackColor = Color.White;

			_settingsHotkey = CreateHotKey(Keys.Pause, SettingsHotkey_Pressed);
			_resetHotkey = CreateHotKey(Keys.Home, ResetHotkey_Pressed);
			_exitHotkey = CreateHotKey(Keys.End, ExitHotkey_Pressed);

			_upHotkey = CreateHotKey(Keys.Up, UpHotkey_Pressed);
			_downHotkey = CreateHotKey(Keys.Down, DownHotkey_Pressed);
			_leftHotkey = CreateHotKey(Keys.Left, LeftHotkey_Pressed);
			_rightHotkey = CreateHotKey(Keys.Right, RightHotkey_Pressed);

			_runOnHotkey = CreateHotKey(Keys.NumLock, RunHotkey_Pressed);
			_runOffHotkey = CreateHotKey(Keys.NumLock, RunHotkey_Pressed);
			_runOffHotkey.Shift = true;

			_keyboard = new InputSimulator().Keyboard;
			
			_refreshTimer.Interval = 1000;
			_refreshTimer.Tick += RefreshTimer_Tick;

			_settingsHotkey.Register(this);
		}

		void RegisterHotkeys() {
			_resetHotkey.Register(this);
			_exitHotkey.Register(this);

			_upHotkey.Register(this);
			_downHotkey.Register(this);
			_leftHotkey.Register(this);
			_rightHotkey.Register(this);
		}
		void UnregisterHotkeys() {
			_resetHotkey.Unregister();
			_exitHotkey.Unregister();

			_upHotkey.Unregister();
			_downHotkey.Unregister();
			_leftHotkey.Unregister();
			_rightHotkey.Unregister();
		}

		void Reticle_Load(object sender, EventArgs e) {
			RefreshPosition();
			_refreshTimer.Start();

			EnableRunFeature();
		}

		void Reticle_FormClosed(object sender, FormClosedEventArgs e) {
			_refreshTimer.Tick -= RefreshTimer_Tick;
			_refreshTimer.Stop();
			_refreshTimer.Dispose();

			DisableRunFeature();

			UnregisterHotkeys();
			_settingsHotkey.Unregister();
		}

		void RefreshPosition() {
			SuspendLayout();

			var handle = RefreshRustHandle();
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

		IntPtr RefreshRustHandle() {
			var processes = Process.GetProcessesByName("rust");
			if (processes.Length > 0) {
				_rustProcess = processes[0];
				_rustHandle = _rustProcess.MainWindowHandle;

				return _rustProcess.MainWindowHandle;
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

		#region Run Feature

		void EnableRunFeature() {
			_runOnHotkey.Register(this);
			_runOffHotkey.Register(this);
		}

		void DisableRunFeature() {
			_runOnHotkey.Unregister();
			_runOffHotkey.Unregister(); 
		}

		void RunHotkey_Pressed(object sender, HandledEventArgs e) {
			InRunMode = !InRunMode;
			ShowWindow(_rustHandle, 1);

			if (Visible && InRunMode) {
				_keyboard.KeyDown(VirtualKeyCode.SHIFT);
				_keyboard.KeyDown(VirtualKeyCode.VK_W);
			} else {
				_keyboard.KeyUp(VirtualKeyCode.SHIFT);
				_keyboard.KeyUp(VirtualKeyCode.VK_W);
			}

			e.Handled = true;
		}

		#endregion

		#region Positioning Feature

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

		#endregion
	}
}