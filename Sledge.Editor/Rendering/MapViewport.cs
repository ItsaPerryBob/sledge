﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using Sledge.DataStructures.Geometric;
using Sledge.Editor.Extensions;
using Sledge.Rendering;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Interfaces;

namespace Sledge.Editor.Rendering
{
    public class MapViewport
    {
        public List<IViewportEventListener> Listeners { get; private set; }
        public IViewport Viewport { get; private set; }

        public Control Control { get { return Viewport.Control; } }
        public int Height { get { return Control.Height; } }
        public int Width { get { return Control.Width; } }

        #region Input Locking

        private object _inputLock;

        public bool IsUnlocked(object context)
        {
            return _inputLock == null || _inputLock == context;
        }

        public bool AquireInputLock(object context)
        {
            if (_inputLock == null) _inputLock = context;
            return _inputLock == context;
        }

        public bool ReleaseInputLock(object context)
        {
            if (_inputLock == context) _inputLock = null;
            return _inputLock == null;
        }

        #endregion
        
        public MapViewport(IViewport viewport)
        {
            Viewport = viewport;
            Listeners = new List<IViewportEventListener>();

            viewport.Control.MouseWheel += OnMouseWheel;
            viewport.Control.MouseEnter += OnMouseEnter;
            viewport.Control.MouseLeave += OnMouseLeave;
            viewport.Control.MouseMove += OnMouseMove;
            viewport.Control.MouseUp += OnMouseUp;
            viewport.Control.MouseDown += OnMouseDown;
            viewport.Control.MouseDoubleClick += OnMouseDoubleClick;
            viewport.Control.KeyDown += OnKeyDown;
            viewport.Control.KeyUp += OnKeyUp;
            viewport.Update += OnUpdate;
        }

        #region Listeners
        
        public delegate void ListenerExceptionEventHandler(object sender, Exception exception);
        public event ListenerExceptionEventHandler ListenerException;
        protected void OnListenerException(Exception ex)
        {
            if (ListenerException != null)
            {
                var st = new StackTrace();
                var frames = st.GetFrames() ?? new StackFrame[0];
                var msg = "Listener exception: " + ex.Message;
                foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    msg += "\r\n    " + method.ReflectedType.FullName + "." + method.Name;
                }
                ListenerException(this, new Exception(msg, ex));
            }
        }

        private void ListenerDo(Action<IViewportEventListener> action)
        {
            foreach (var listener in Listeners)
            {
                try
                {
                    action(listener);
                }
                catch (Exception ex)
                {
                    OnListenerException(ex);
                }
            }
        }

        private void ListenerDoEvent(ViewportEvent e, Action<IViewportEventListener, ViewportEvent> action)
        {
            foreach (var listener in Listeners)
            {
                try
                {
                    action(listener, e);
                }
                catch (Exception ex)
                {
                    OnListenerException(ex);
                }
                if (e.Handled)
                {
                    break;
                }
            }
        }


        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.MouseWheel(v));
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.MouseEnter(v));
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.MouseLeave(v));
            _lastMouseLocationKnown = false;
            _lastMouseLocation = new Point(-1, -1);
        }

        private bool _dragging = false;
        private MouseButtons _dragButton;
        private bool _lastMouseLocationKnown = false;
        private Point _lastMouseLocation = new Point(-1, -1);
        private Point _mouseDownLocation = new Point(-1, -1);

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_lastMouseLocationKnown)
            {
                _lastMouseLocation = new Point(e.X, e.Y);
            }
            var ve = new ViewportEvent(this, e)
            {
                Dragging = _dragging,
                StartX = _mouseDownLocation.X,
                StartY = _mouseDownLocation.Y,
                LastX = _lastMouseLocation.X,
                LastY = _lastMouseLocation.Y,
            };
            if (!_dragging
                && (Math.Abs(_mouseDownLocation.X - e.Location.X) > 1
                    || Math.Abs(_mouseDownLocation.Y - e.Location.Y) > 1)
                && _mouseDownLocation.X >= 0 && _mouseDownLocation.Y >= 0)
            {
                _dragging = ve.Dragging = true;
                ve.Button = _dragButton;
                ListenerDoEvent(ve, (l, v) => l.DragStart(v));
            }
            ListenerDoEvent(ve, (l, v) => l.MouseMove(v));
            if (_dragging)
            {
                ve.Button = _dragButton;
                ListenerDoEvent(ve, (l, v) => l.DragMove(v));
            }
            _lastMouseLocationKnown = true;
            _lastMouseLocation = new Point(e.X, e.Y);
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (!_lastMouseLocationKnown)
            {
                _lastMouseLocation = new Point(e.X, e.Y);
            }
            var ve = new ViewportEvent(this, e)
            {
                Dragging = _dragging,
                StartX = _mouseDownLocation.X,
                StartY = _mouseDownLocation.Y,
                LastX = _lastMouseLocation.X,
                LastY = _lastMouseLocation.Y,
            };
            if (_dragging && ve.Button == _dragButton)
            {
                ListenerDoEvent(ve, (l, v) => l.DragEnd(v));
            }
            ListenerDoEvent(ve, (l, v) => l.MouseUp(v));
            if (!_dragging
                && Math.Abs(_mouseDownLocation.X - e.Location.X) <= 1
                && Math.Abs(_mouseDownLocation.Y - e.Location.Y) <= 1)
            {
                // Mouse hasn't moved very much, trigger the click event
                ListenerDoEvent(ve, (l, v) => l.MouseClick(v));
            }
            if (_dragging && ve.Button == _dragButton)
            {
                _dragging = false;
            }
            if (!_dragging)
            {
                _mouseDownLocation = new Point(-1, -1);
            }
            _lastMouseLocationKnown = true;
            _lastMouseLocation = new Point(e.X, e.Y);
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (!_lastMouseLocationKnown)
            {
                _lastMouseLocation = new Point(e.X, e.Y);
            }
            if (!_dragging)
            {
                _mouseDownLocation = new Point(e.X, e.Y);
                _dragging = false;
                _dragButton = e.Button;
            }
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.MouseDown(v));
            _lastMouseLocationKnown = true;
            _lastMouseLocation = new Point(e.X, e.Y);
        }

        private void OnMouseDoubleClick(object sender, EventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.MouseDoubleClick(v));
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.KeyDown(v));
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            ListenerDoEvent(new ViewportEvent(this, e), (l, v) => l.KeyUp(v));
        }

        private void OnUpdate(object sender, Frame frame)
        {
            ListenerDo(x => x.UpdateFrame(frame));
        }

        #endregion

        #region 2D/3D methods

        public bool Is2D
        {
            get { return Viewport.Camera is OrthographicCamera; }
        }

        public bool Is3D
        {
            get { return Viewport.Camera is PerspectiveCamera; }
        }

        public Coordinate CenterScreen
        {
            get { return new Coordinate(Control.Width / 2m, Control.Height / 2m, 0); }
        }

        public float Zoom
        {
            get {
                return Is2D ? ((OrthographicCamera) Viewport.Camera).Zoom : 1;
            }
        }

        public OrthographicCamera.OrthographicType Direction
        {
            get { return Is2D ? ((OrthographicCamera) Viewport.Camera).Type : OrthographicCamera.OrthographicType.Top; }
        }

        public Coordinate Flatten(Coordinate c)
        {
            if (!Is2D) return c;
            var dir = ((OrthographicCamera)Viewport.Camera).Type;
            switch (dir)
            {
                case OrthographicCamera.OrthographicType.Top:
                    return new Coordinate(c.X, c.Y, 0);
                case OrthographicCamera.OrthographicType.Front:
                    return new Coordinate(c.Y, c.Z, 0);
                case OrthographicCamera.OrthographicType.Side:
                    return new Coordinate(c.X, c.Z, 0);
                default:
                    throw new ArgumentOutOfRangeException("Type");
            }
        }

        public Coordinate Expand(Coordinate c)
        {
            if (!Is2D) return c;
            var dir = ((OrthographicCamera) Viewport.Camera).Type;
            switch (dir)
            {
                case OrthographicCamera.OrthographicType.Top:
                    return new Coordinate(c.X, c.Y, 0);
                case OrthographicCamera.OrthographicType.Front:
                    return new Coordinate(0, c.X, c.Y);
                case OrthographicCamera.OrthographicType.Side:
                    return new Coordinate(c.X, 0, c.Y);
                default:
                    throw new ArgumentOutOfRangeException("Type");
            }
        }

        public Coordinate GetUnusedCoordinate(Coordinate c)
        {
            if (!Is2D) return c;
            var dir = ((OrthographicCamera)Viewport.Camera).Type;
            switch (dir)
            {
                case OrthographicCamera.OrthographicType.Top:
                    return new Coordinate(0, 0, c.Z);
                case OrthographicCamera.OrthographicType.Front:
                    return new Coordinate(c.X, 0, 0);
                case OrthographicCamera.OrthographicType.Side:
                    return new Coordinate(0, c.Y, 0);
                default:
                    throw new ArgumentOutOfRangeException("Type");
            }
        }

        public Coordinate ZeroUnusedCoordinate(Coordinate c)
        {
            if (!Is2D) return c;
            var dir = ((OrthographicCamera)Viewport.Camera).Type;
            switch (dir)
            {
                case OrthographicCamera.OrthographicType.Top:
                    return new Coordinate(c.X, c.Y, 0);
                case OrthographicCamera.OrthographicType.Front:
                    return new Coordinate(0, c.Y, c.Z);
                case OrthographicCamera.OrthographicType.Side:
                    return new Coordinate(c.X, 0, c.Z);
                default:
                    throw new ArgumentOutOfRangeException("Type");
            }
        }

        public Coordinate ScreenToWorld(Point location)
        {
            return ScreenToWorld(location.X, location.Y);
        }

        public Coordinate ScreenToWorld(decimal x, decimal y)
        {
            return ScreenToWorld(new Coordinate(x, y, 0));
        }

        public Coordinate ScreenToWorld(Coordinate location)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera) Viewport.Camera;
                return cam.Position.ToCoordinate() + ((location - CenterScreen) / (decimal) cam.Zoom);
            }
            else
            {
                var cam = (PerspectiveCamera) Viewport.Camera;
                var screen = new Coordinate(location.X, location.Y, 1);
                var viewport = new[] { 0, 0, Control.Width, Control.Height };
                var pm = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(cam.FOV), Control.Width / (float) Control.Height, 0.1f, 50000);
                var vm = Matrix4.LookAt(cam.Position, cam.LookAt, Vector3.UnitZ);
                return MathFunctions.Unproject(screen, viewport, pm, vm);
            }
        }

        public Coordinate WorldToScreen(Coordinate location)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera) Viewport.Camera;
                return CenterScreen + ((location - cam.Position.ToCoordinate()) * (decimal) cam.Zoom);
            }
            else
            {
                var cam = (PerspectiveCamera)Viewport.Camera;
                var viewport = new[] { 0, 0, Control.Width, Control.Height };
                var pm = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(cam.FOV), Control.Width / (float)Control.Height, 0.1f, 50000);
                var vm = Matrix4.LookAt(cam.Position, cam.LookAt, Vector3.UnitZ);
                return MathFunctions.Project(location, viewport, pm, vm);
            }
        }

        public decimal UnitsToPixels(decimal units)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera)Viewport.Camera;
                return units * (decimal) cam.Zoom;
            }
            else
            {
                return 0;
            }
        }

        public decimal PixelsToUnits(decimal pixels)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera)Viewport.Camera;
                return pixels / (decimal) cam.Zoom;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Project the 2D coordinates from the screen coordinates outwards
        /// from the camera along the lookat vector, taking the frustrum
        /// into account. The resulting line will be run from the camera
        /// position along the view axis and end at the back clipping pane.
        /// </summary>
        /// <param name="x">The X coordinate on screen</param>
        /// <param name="y">The Y coordinate on screen</param>
        /// <returns>A line beginning at the camera location and tracing
        /// along the 3D projection for at least 1,000,000 units.</returns>
        public Line CastRayFromScreen(int x, int y)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera)Viewport.Camera;
                var world = cam.Position.ToCoordinate() + ((new Coordinate(x, y, 0) - CenterScreen) / (decimal)cam.Zoom);
                return null; // todo
            }
            else
            {
                var cam = (PerspectiveCamera)Viewport.Camera;
                var near = new Coordinate(x, Control.Height - y, 0);
                var far = new Coordinate(x, Control.Height - y, 1);
                var pm = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(cam.FOV), Control.Width / (float)Control.Height, 0.1f, 50000);
                var vm = Matrix4.LookAt(cam.Position, cam.LookAt, Vector3.UnitZ);
                var viewport = new[] { 0, 0, Control.Width, Control.Height };
                var un = MathFunctions.Unproject(near, viewport, pm, vm);
                var uf = MathFunctions.Unproject(far, viewport, pm, vm);
                return (un == null || uf == null) ? null : new Line(un, uf);
            }
        }

        #endregion

        #region Camera Manipulation
        
        public void FocusOn(Box box)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera)Viewport.Camera;
                cam.Position = Flatten(box.Center).ToVector3();
            }
            else
            {
                var cam = (PerspectiveCamera)Viewport.Camera;
                var dist = System.Math.Max(System.Math.Max(box.Width, box.Length), box.Height);
                var normal = cam.Position - cam.LookAt;
                var v = new Vector(normal.ToCoordinate(), dist);
                FocusOn(box.Center, new Coordinate(v.X, v.Y, v.Z));
            }
        }

        public void FocusOn(Coordinate coordinate)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera) Viewport.Camera;
                cam.Position = Flatten(coordinate).ToVector3();
            }
            else
            {
                FocusOn(coordinate, Coordinate.UnitY * -100);
            }
        }

        public void FocusOn(Coordinate coordinate, Coordinate distance)
        {
            if (Is2D)
            {
                var cam = (OrthographicCamera)Viewport.Camera;
                cam.Position = Flatten(coordinate).ToVector3();
            }
            else
            {
                var cam = (PerspectiveCamera)Viewport.Camera;
                var pos = coordinate + distance;
                cam.Position = pos.ToVector3();
                cam.LookAt = coordinate.ToVector3();
            }
        }

        #endregion
    }
}