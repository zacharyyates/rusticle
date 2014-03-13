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
        struct RECT {
            public int Left; // x position of upper-left corner
            public int Top; // y position of upper-left corner
            public int Right; // x position of lower-right corner
            public int Bottom; // y position of lower-right corner
        }

        // http://msdn.microsoft.com/en-us/library/windows/desktop/ms633548(v=vs.85).aspx
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int SW_SHOW = 5;  // Activates the window and displays it in its current size and position.

        readonly Timer _refreshTimer = new Timer();

        Process _rustProcess;
        IntPtr _rustHandle = IntPtr.Zero;

        bool InSettingsMode {
            get { return _resetHotkey.Registered; }
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

        int _reticleIndex;
        bool _reticleEnabled;
        readonly List<Image> _reticleImages = new List<Image>();

        readonly IKeyboardSimulator _keyboard;

        readonly Hotkey _settingsHotkey;
        readonly Hotkey _resetHotkey;
        readonly Hotkey _exitHotkey;

        readonly Hotkey _upHotkey;
        readonly Hotkey _downHotkey;
        readonly Hotkey _leftHotkey;
        readonly Hotkey _rightHotkey;

        readonly Hotkey _runOnHotkey;
        readonly Hotkey _runOffHotkey;
        readonly Hotkey _wHotkey;
        readonly Hotkey _aHotkey;
        readonly Hotkey _sHotkey;
        readonly Hotkey _dHotkey;
        readonly Hotkey _ctrlHotkey;

        readonly Hotkey _disableReticleHotkey;
        readonly Hotkey _cycleReticleHotkey;
        
        public Reticle() {
            InitializeComponent();
            
            _exitHotkey = CreateHotkey(Keys.End, ExitHotkey_Pressed);

            _settingsHotkey = CreateHotkey(Keys.Pause, SettingsHotkey_Pressed);
            _resetHotkey = CreateHotkey(Keys.Home, ResetHotkey_Pressed);
            _upHotkey = CreateHotkey(Keys.Up, UpHotkey_Pressed);
            _downHotkey = CreateHotkey(Keys.Down, DownHotkey_Pressed);
            _leftHotkey = CreateHotkey(Keys.Left, LeftHotkey_Pressed);
            _rightHotkey = CreateHotkey(Keys.Right, RightHotkey_Pressed);

            _runOnHotkey = CreateHotkey(Keys.CapsLock, ToggleAutorun);
            _runOffHotkey = CreateHotkey(Keys.CapsLock, ToggleAutorun, shift: true);
            _wHotkey = CreateHotkey(Keys.W, StopAutorun, shift: true);
            _aHotkey = CreateHotkey(Keys.A, StopAutorun, shift: true);
            _sHotkey = CreateHotkey(Keys.S, StopAutorun, shift: true);
            _dHotkey = CreateHotkey(Keys.D, StopAutorun, shift: true);
            _ctrlHotkey = CreateHotkey(Keys.LControlKey, StopAutorun, shift: true);

            _disableReticleHotkey = CreateHotkey(Keys.Delete, DisableReticleHotkey_Pressed);
            _cycleReticleHotkey = CreateHotkey(Keys.Insert, CycleReticleHotkey_Pressed);

            _keyboard = new InputSimulator().Keyboard;

            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += RefreshTimer_Tick;

            // load reticle images
            var path = Path.Combine(Environment.CurrentDirectory, "img");
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

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
            if (InSettingsMode) {
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
            var handle = RefreshRustHandle();
            RECT rustWindow;

            if (_reticleEnabled &&
                handle != IntPtr.Zero &&
                GetWindowRect(new HandleRef(this, handle), out rustWindow)) {

                var rustWidth = rustWindow.Right - rustWindow.Left;
                var rustHeight = rustWindow.Bottom - rustWindow.Top;

                var offsetX = Width / 2;

                var left = rustWindow.Left - offsetX + (rustWidth / 2) + OffsetX;
                var top = rustWindow.Top + (rustHeight / 2) + OffsetY;

                SuspendLayout();

                Location = new Point(left, top);
                Width = BackgroundImage.Width;
                Height = BackgroundImage.Height;

                ResumeLayout();
                SetReticleVisibility(true);
            } else {
                SetReticleVisibility(false);
            }
        }

        void SetReticleVisibility(bool visible) {
            Visible = visible;
            TopMost = visible;
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

        Hotkey CreateHotkey(Keys keys, HandledEventHandler handler, bool control = false, bool shift = false) {
            var hotkey = new Hotkey {
                KeyCode = keys,
                Control = control,
                Shift = shift
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

        void ToggleAutorun(object sender, HandledEventArgs e) {
            _inRunMode = !_inRunMode;

            if (!_inRunMode) {
                StartAutorun(sender, e);
            } else {
                StopAutorun(sender, e);
            }
        }

        void StartAutorun(object sender, HandledEventArgs e) {
            ShowWindow(_rustHandle, SW_SHOW);
            _keyboard.KeyDown(VirtualKeyCode.SHIFT);
            _keyboard.KeyDown(VirtualKeyCode.VK_W);

            RegisterAutorunKeys();

            _inRunMode = true;
            e.Handled = true;
        }

        void StopAutorun(object sender, HandledEventArgs e) {
            UnregisterAutorunKeys();

            ShowWindow(_rustHandle, SW_SHOW);
            _keyboard.KeyUp(VirtualKeyCode.SHIFT);
            _keyboard.KeyUp(VirtualKeyCode.VK_W);

            _inRunMode = false;
            e.Handled = true;
        }

        void RegisterAutorunKeys() {
            //_wHotkey.Register(this);
            _aHotkey.Register(this);
            _sHotkey.Register(this);
            _dHotkey.Register(this);
            _ctrlHotkey.Register(this);
        }
        void UnregisterAutorunKeys() {
            if (_inRunMode) {
                //_wHotkey.Unregister();
                _aHotkey.Unregister();
                _sHotkey.Unregister();
                _dHotkey.Unregister();
                _ctrlHotkey.Unregister();
            }
        }

        #endregion

        #region Positioning Feature

        void RefreshTimer_Tick(object sender, EventArgs e) {
            RefreshReticle();
        }

        void SettingsHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode)
                UnregisterSettingsKeys();
            else
                RegisterSettingsKeys();
        }

        void ResetHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                e.Handled = true;
                // best guess for center screen
                OffsetX = 1;
                OffsetY = 6;
                RefreshReticle();
            }
        }

        void UpHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                e.Handled = true;
                OffsetY -= 1;
                RefreshReticle();
            }
        }

        void DownHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                e.Handled = true;
                OffsetY += 1;
                RefreshReticle();
            }
        }

        void LeftHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                e.Handled = true;
                OffsetX -= 1;
                RefreshReticle();
            }
        }

        void RightHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                e.Handled = true;
                OffsetX += 1;
                RefreshReticle();
            }
        }

        #endregion

        #region User Reticles Feature

        void DisableReticleHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                _reticleEnabled = !_reticleEnabled;
                RefreshReticle();
            }
        }

        void CycleReticleHotkey_Pressed(object sender, HandledEventArgs e) {
            if (InSettingsMode) {
                if (!_reticleEnabled) {
                    _reticleEnabled = true;
                }

                if (_reticleImages.Count > 0) {
                    _reticleIndex = _reticleIndex < _reticleImages.Count - 1
                        ? _reticleIndex + 1
                        : 0;

                    BackgroundImage = _reticleImages[_reticleIndex];

                    RefreshReticle();
                }
            }
        }

        #endregion
    }
}