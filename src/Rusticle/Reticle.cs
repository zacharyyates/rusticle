using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MovablePython;
using WindowsInput;
using WindowsInput.Native;

namespace Rusticle {
    public partial class Reticle : Form {

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(HandleRef hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT {
            public int Left; // x position of upper-left corner
            public int Top; // y position of upper-left corner
            public int Right; // x position of lower-right corner
            public int Bottom; // y position of lower-right corner
        }

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        Timer _refreshTimer = new Timer();

        Process _rustProcess;
        IntPtr _rustHandle = IntPtr.Zero;

        bool _inSettingsMode;

        object _lockRunMode = new object();
        bool InRunMode {
            get {
                lock (_lockRunMode) {
                    return _inRunMode;
                }
            }
            set {
                lock (_lockRunMode) {
                    _inRunMode = value;
                }
            }
        }
        bool _inRunMode;

        int OffsetX {
            get { return Properties.Settings.Default.OffsetX; }
            set { Properties.Settings.Default.OffsetX = value; }
        }

        int OffsetY {
            get { return Properties.Settings.Default.OffsetY; }
            set { Properties.Settings.Default.OffsetY = value; }
        }

        List<Image> _reticleImages = new List<Image>();
        int _reticleIndex;
        bool _reticleEnabled = true;

        IKeyboardSimulator _keyboard;

        Hotkey _settingsHotkey;
        Hotkey _resetHotkey;
        Hotkey _exitHotkey;

        Hotkey _upHotkey;
        Hotkey _downHotkey;
        Hotkey _leftHotkey;
        Hotkey _rightHotkey;

        Hotkey _runOnHotkey;
        Hotkey _runOffHotkey;

        Hotkey _disableReticleHotkey;
        Hotkey _cycleReticleHotkey;
        
        public Reticle() {
            InitializeComponent();

            _exitHotkey = CreateHotkey(Keys.End, ExitHotkey_Pressed);

            _settingsHotkey = CreateHotkey(Keys.Pause, SettingsHotkey_Pressed);
            _resetHotkey = CreateHotkey(Keys.Home, ResetHotkey_Pressed);
            _upHotkey = CreateHotkey(Keys.Up, UpHotkey_Pressed);
            _downHotkey = CreateHotkey(Keys.Down, DownHotkey_Pressed);
            _leftHotkey = CreateHotkey(Keys.Left, LeftHotkey_Pressed);
            _rightHotkey = CreateHotkey(Keys.Right, RightHotkey_Pressed);

            _runOnHotkey = CreateHotkey(Keys.NumLock, RunHotkey_Pressed);
            _runOffHotkey = CreateHotkey(Keys.NumLock, RunHotkey_Pressed);
            _runOffHotkey.Shift = true;

            _disableReticleHotkey = CreateHotkey(Keys.Delete, DisableReticleHotkey_Pressed);
            _cycleReticleHotkey = CreateHotkey(Keys.Insert, CycleReticleHotkey_Pressed);

            _keyboard = new InputSimulator().Keyboard;

            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += RefreshTimer_Tick;

            // load reticle images
            var path = Path.Combine(Environment.CurrentDirectory, "img");
            var files = Directory.GetFiles(path, "*.png");
            foreach (var file in files) {
                _reticleImages.Add(Image.FromFile(file));
            }
        }

        void RegisterSettingsKeys() {
            _resetHotkey.Register(this);
            _upHotkey.Register(this);
            _downHotkey.Register(this);
            _leftHotkey.Register(this);
            _rightHotkey.Register(this);
            _disableReticleHotkey.Register(this);
            _cycleReticleHotkey.Register(this);
        }

        void UnregisterSettingsKeys() {
            if (_inSettingsMode) {
                _resetHotkey.Unregister();
                _upHotkey.Unregister();
                _downHotkey.Unregister();
                _leftHotkey.Unregister();
                _rightHotkey.Unregister();
                _disableReticleHotkey.Unregister();
                _cycleReticleHotkey.Unregister();
            }
        }

        void Reticle_Load(object sender, EventArgs e) {
            Opacity = 0.55;
            TransparencyKey = Color.White;
            BackColor = Color.White;

            RefreshReticle();

            _refreshTimer.Start();
            _exitHotkey.Register(this);
            _settingsHotkey.Register(this);

            EnableRunFeature();
        }

        void Reticle_FormClosed(object sender, FormClosedEventArgs e) {
            _refreshTimer.Tick -= RefreshTimer_Tick;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();

            DisableRunFeature();

            UnregisterSettingsKeys();
            _settingsHotkey.Unregister();
            _exitHotkey.Unregister();
        }

        void RefreshReticle() {
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
            Width = BackgroundImage.Width;
            Height = BackgroundImage.Height;

            Visible = _reticleEnabled;
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

        Hotkey CreateHotkey(Keys keys, HandledEventHandler handler, bool control = false) {
            var hotkey = new Hotkey {
                KeyCode = keys,
                Control = control
            };
            hotkey.Pressed += handler;
            return hotkey;
        }

        void ExitHotkey_Pressed(object sender, HandledEventArgs e) {
            e.Handled = true;
            Close();
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
            RefreshReticle();
        }

        void SettingsHotkey_Pressed(object sender, HandledEventArgs e) {
            _inSettingsMode = !_inSettingsMode;

            if (_inSettingsMode)
                RegisterSettingsKeys();
            else
                UnregisterSettingsKeys();
        }

        void ResetHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                e.Handled = true;
                // best guess for center screen
                OffsetX = 1;
                OffsetY = 6;
                RefreshReticle();
            }
        }

        void UpHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                e.Handled = true;
                OffsetY -= 1;
                RefreshReticle();
            }
        }

        void DownHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                e.Handled = true;
                OffsetY += 1;
                RefreshReticle();
            }
        }

        void LeftHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                e.Handled = true;
                OffsetX -= 1;
                RefreshReticle();
            }
        }

        void RightHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                e.Handled = true;
                OffsetX += 1;
                RefreshReticle();
            }
        }

        #endregion

        #region User Reticles Feature

        void DisableReticleHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                _reticleEnabled = !_reticleEnabled;
                RefreshReticle();
            }
        }

        void CycleReticleHotkey_Pressed(object sender, HandledEventArgs e) {
            if (_inSettingsMode) {
                if (!_reticleEnabled) {
                    _reticleEnabled = true;
                }

                _reticleIndex = _reticleIndex < _reticleImages.Count - 1
                    ? _reticleIndex + 1
                    : 0;

                BackgroundImage = _reticleImages[_reticleIndex];

                RefreshReticle();
            }
        }

        #endregion
    }
}