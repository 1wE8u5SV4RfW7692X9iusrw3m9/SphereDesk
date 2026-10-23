using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;

namespace SphereDesk.Core.Engine;

public enum InteractionMode
{
    IdleAutoRotate,
    HoverPaused,
    Dragging,
    Inertia,
    SearchFocus,
    MenuOpen,
    Suspended,
}

public sealed record SphereFrameInput
{
    public double ElapsedSeconds { get; init; }
    public bool WindowInteractive { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public bool AutoRotateEnabled { get; init; } = true;
    public double AngularSpeedRadiansPerSecond { get; init; } = 0.15;
    public bool PointerOverScene { get; init; }
    public bool PrimaryDown { get; init; }
    public bool PrimaryPressed { get; init; }
    public bool PrimaryReleased { get; init; }
    public bool CaptureLost { get; init; }
    public double PointerX { get; init; }
    public double PointerY { get; init; }
    public int ClickCount { get; init; } = 1;
    public double DragThresholdDip { get; init; } = 4;
    public double ViewportWidth { get; init; } = 800;
    public double ViewportHeight { get; init; } = 600;
    public bool SearchActive { get; init; }
    public bool MenuActive { get; init; }
    public int AlignToken { get; init; }
    public string? AlignItemId { get; init; }
    public IReadOnlyList<LayoutSlot> Slots { get; init; } = [];
    public bool FrontRest { get; init; }
    public double IconRadiusDip { get; init; } = 28;
    public double LabelMarginDip { get; init; } = 18;
}

public sealed record SphereFrame(
    InteractionMode Mode,
    Quaternion Pose,
    IReadOnlyList<ProjectedItem> Items,
    string? HoveredItemId,
    string? SelectedItemId,
    bool OpenRequested,
    string? OpenItemId,
    bool CaptureRequested,
    bool CaptureReleaseRequested,
    bool NeedsContinuousRender);

public interface ISphereEngine
{
    SphereFrame Update(SphereFrameInput input);
}

public sealed class SphereEngine : ISphereEngine
{
    public const double MaxStepSeconds = 0.05;
    public const double MaxAngularSpeed = SettingsLimits.MaxAngularSpeed;
    public const double FrontWobbleRadians = 1.25 * Math.PI / 180;
    public const double FrontWobblePeriodSeconds = 10;
    private const double AlignSeconds = 0.45;

    private Quaternion _pose = Quaternion.Identity;
    private Quaternion _poseAtDragStart = Quaternion.Identity;
    private Quaternion _alignFrom = Quaternion.Identity;
    private Quaternion _alignTo = Quaternion.Identity;
    private Vector3 _trackballOrigin = Vector3.UnitZ;
    private Vector3 _lastTrackball = Vector3.UnitZ;
    private Vector3 _velocityAxis = Vector3.UnitY;
    private double _velocitySpeed;
    private double _alignT;
    private bool _aligning;
    private bool _gestureDown;
    private bool _dragLatched;
    private bool _armOpen;
    private bool _inertiaActive;
    private bool _frontRest;
    private bool _settling;
    private double _wobblePhase;
    private double _settleT;
    private Quaternion _settleFrom = Quaternion.Identity;
    private Quaternion _settleTo = Quaternion.Identity;
    private double _pressX;
    private double _pressY;
    private string? _pressHitId;
    private string? _selectedId;
    private string? _hoveredId;
    private int _lastAlignToken;

    public SphereFrame Update(SphereFrameInput input)
    {
        var elapsed = double.IsFinite(input.ElapsedSeconds) ? Math.Clamp(input.ElapsedSeconds, 0, MaxStepSeconds) : 0;
        var speed = double.IsFinite(input.AngularSpeedRadiansPerSecond)
            ? Math.Clamp(input.AngularSpeedRadiansPerSecond, 0, MaxAngularSpeed)
            : 0;
        var blocking = !input.WindowInteractive || input.MenuActive || input.SearchActive;
        if (input.FrontRest != _frontRest)
        {
            var entered = input.FrontRest;
            _frontRest = input.FrontRest;
            if (entered && !_gestureDown && !_dragLatched)
            {
                _pose = Quaternion.Identity;
                _wobblePhase = 0;
                _settling = false;
                _aligning = false;
                _inertiaActive = false;
                _velocitySpeed = 0;
            }

            if (!entered)
            {
                _settling = false;
            }
        }

        if (input.CaptureLost || blocking)
        {
            var settle = input.FrontRest && _dragLatched;
            EndGesture(startInertia: false);
            if (settle)
            {
                BeginFrontSettle(input.ReducedMotion);
            }
        }

        if (!blocking && _dragLatched && input.PrimaryDown)
        {
            ApplyTrackball(input, elapsed);
        }
        else if (!input.FrontRest && !blocking && _inertiaActive && !input.ReducedMotion)
        {
            ApplyInertia(elapsed);
        }
        else if (_settling && input.FrontRest)
        {
            AdvanceFrontSettle(elapsed, input.ReducedMotion);
        }
        else if (AllowFrontWobble(input))
        {
            var turn = Math.PI * 2;
            _wobblePhase += elapsed * (turn / FrontWobblePeriodSeconds);
            if (_wobblePhase >= turn)
            {
                _wobblePhase %= turn;
            }

            _pose = FrontWobble(_wobblePhase);
        }
        else if (!input.FrontRest && !blocking && !_gestureDown && !_aligning && input.AutoRotateEnabled && !input.ReducedMotion && !input.PointerOverScene)
        {
            _pose = (Quaternion.FromAxisAngle(Vector3.UnitY, speed * elapsed) * _pose).Normalized();
        }
        else if (input.FrontRest && !_gestureDown && !_dragLatched && (input.ReducedMotion || !input.AutoRotateEnabled || !input.WindowInteractive || input.MenuActive))
        {
            _pose = Quaternion.Identity;
            _wobblePhase = 0;
        }

        if (input.AlignToken != _lastAlignToken && !string.IsNullOrEmpty(input.AlignItemId) && !_dragLatched && !_gestureDown && !input.FrontRest)
        {
            _lastAlignToken = input.AlignToken;
            BeginAlign(input.AlignItemId, input.Slots, input.ReducedMotion);
        }

        if (_aligning)
        {
            AdvanceAlign(elapsed, input.ReducedMotion);
        }

        var projected = Project(input);
        var hit = input.PointerOverScene ? HitTester.Find(projected, input.PointerX, input.PointerY) : null;
        var captureRequested = false;
        var captureReleaseRequested = false;
        var openRequested = false;
        string? openItemId = null;

        if (!blocking && input.PrimaryPressed)
        {
            _inertiaActive = false;
            _velocitySpeed = 0;
            _aligning = false;
            _settling = false;
            _gestureDown = true;
            _dragLatched = false;
            _pressX = input.PointerX;
            _pressY = input.PointerY;
            _pressHitId = hit;
            _poseAtDragStart = _pose;
            _trackballOrigin = Trackball.Map(input.PointerX, input.PointerY, input.ViewportWidth, input.ViewportHeight);
            _lastTrackball = _trackballOrigin;
            _armOpen = input.ClickCount >= 2 && hit is not null && hit == _selectedId;
            if (hit is not null)
            {
                _selectedId = hit;
            }
        }

        if (!blocking && _gestureDown && input.PrimaryDown && !_dragLatched)
        {
            var dx = input.PointerX - _pressX;
            var dy = input.PointerY - _pressY;
            var threshold = Math.Max(0, input.DragThresholdDip);
            if ((dx * dx) + (dy * dy) >= threshold * threshold)
            {
                _dragLatched = true;
                _armOpen = false;
                _inertiaActive = false;
                _aligning = false;
                captureRequested = true;
                ApplyTrackball(input, elapsed);
                projected = Project(input);
                hit = input.PointerOverScene ? HitTester.Find(projected, input.PointerX, input.PointerY) : null;
            }
        }

        if (!blocking && input.PrimaryReleased && _gestureDown)
        {
            if (_dragLatched)
            {
                var front = input.FrontRest;
                EndGesture(startInertia: !input.ReducedMotion && !front);
                if (front)
                {
                    BeginFrontSettle(input.ReducedMotion);
                }

                captureReleaseRequested = true;
            }
            else if (_armOpen && _pressHitId is not null && projected.Any(item => item.ItemId == _pressHitId && !item.IsBackFacing))
            {
                openRequested = true;
                openItemId = _pressHitId;
                _selectedId = _pressHitId;
                _armOpen = false;
                _gestureDown = false;
            }
            else
            {
                if (hit is not null)
                {
                    _selectedId = hit;
                }

                _gestureDown = false;
                _armOpen = false;
            }
        }

        _hoveredId = input.PointerOverScene ? hit : null;
        projected = SphereProjector.ApplyLabels(projected, _hoveredId, _selectedId);
        var mode = ResolveMode(input);
        var wobbling = AllowFrontWobble(input);
        var animating = _aligning || _settling || wobbling || mode is InteractionMode.IdleAutoRotate or InteractionMode.Dragging or InteractionMode.Inertia;
        return new SphereFrame(
            mode,
            _pose,
            projected,
            _hoveredId,
            _selectedId,
            openRequested,
            openItemId,
            captureRequested,
            captureReleaseRequested,
            animating && input.WindowInteractive);
    }

    private IReadOnlyList<ProjectedItem> Project(SphereFrameInput input) =>
        SphereProjector.Project(
            _pose,
            input.Slots,
            input.ViewportWidth,
            input.ViewportHeight,
            input.IconRadiusDip,
            input.LabelMarginDip,
            _hoveredId,
            _selectedId);

    private void ApplyTrackball(SphereFrameInput input, double elapsed)
    {
        var current = Trackball.Map(input.PointerX, input.PointerY, input.ViewportWidth, input.ViewportHeight);
        var delta = Quaternion.FromTo(_trackballOrigin, current);
        _pose = (delta * _poseAtDragStart).Normalized();
        var step = Quaternion.FromTo(_lastTrackball, current);
        if (step.TryGetAxisAngle(out var axis, out var angle))
        {
            var divisor = Math.Max(elapsed, 0.001);
            _velocityAxis = axis;
            _velocitySpeed = Math.Clamp(angle / divisor, 0, 12);
        }

        _lastTrackball = current;
    }

    private bool AllowFrontWobble(SphereFrameInput input) =>
        input.FrontRest
        && input.WindowInteractive
        && !input.MenuActive
        && !_gestureDown
        && !_dragLatched
        && !_aligning
        && !_settling
        && input.AutoRotateEnabled
        && !input.ReducedMotion
        && !input.PointerOverScene;

    private static Quaternion FrontWobble(double phase)
    {
        var tiltX = Quaternion.FromAxisAngle(Vector3.UnitX, FrontWobbleRadians * Math.Cos(phase));
        var tiltY = Quaternion.FromAxisAngle(Vector3.UnitY, FrontWobbleRadians * Math.Sin(phase));
        return (tiltX * tiltY).Normalized();
    }

    private void BeginFrontSettle(bool reducedMotion)
    {
        _inertiaActive = false;
        _velocitySpeed = 0;
        _wobblePhase = 0;
        _settleFrom = _pose;
        _settleTo = Quaternion.Identity;
        _settleT = 0;
        if (reducedMotion)
        {
            _pose = Quaternion.Identity;
            _settling = false;
            return;
        }

        _settling = true;
    }

    private void AdvanceFrontSettle(double elapsed, bool reducedMotion)
    {
        if (reducedMotion)
        {
            _pose = Quaternion.Identity;
            _settling = false;
            _wobblePhase = 0;
            return;
        }

        _settleT += elapsed / AlignSeconds;
        var amount = _settleT <= 0 ? 0 : _settleT >= 1 ? 1 : (_settleT * _settleT) * (3 - (2 * _settleT));
        _pose = Quaternion.Slerp(_settleFrom, _settleTo, amount);
        if (_settleT >= 1)
        {
            _pose = Quaternion.Identity;
            _settling = false;
            _wobblePhase = 0;
        }
    }

    private void ApplyInertia(double elapsed)
    {
        if (_velocitySpeed < 0.02)
        {
            _inertiaActive = false;
            _velocitySpeed = 0;
            return;
        }

        _pose = (Quaternion.FromAxisAngle(_velocityAxis, _velocitySpeed * elapsed) * _pose).Normalized();
        _velocitySpeed *= Math.Pow(0.08, elapsed);
        if (_velocitySpeed < 0.02)
        {
            _inertiaActive = false;
            _velocitySpeed = 0;
        }
    }

    private void BeginAlign(string itemId, IReadOnlyList<LayoutSlot> slots, bool reducedMotion)
    {
        var slot = slots.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (slot is null)
        {
            return;
        }

        var current = _pose.Rotate(slot.UnitPosition.Normalized());
        var delta = Quaternion.FromTo(current, Vector3.UnitZ);
        _alignFrom = _pose;
        _alignTo = (delta * _pose).Normalized();
        _inertiaActive = false;
        _velocitySpeed = 0;
        if (reducedMotion)
        {
            _pose = _alignTo;
            _aligning = false;
            _alignT = 1;
            return;
        }

        _alignT = 0;
        _aligning = true;
    }

    private void AdvanceAlign(double elapsed, bool reducedMotion)
    {
        if (reducedMotion)
        {
            _pose = _alignTo;
            _aligning = false;
            return;
        }

        _alignT += elapsed / AlignSeconds;
        var amount = _alignT <= 0 ? 0 : _alignT >= 1 ? 1 : (_alignT * _alignT) * (3 - (2 * _alignT));
        _pose = Quaternion.Slerp(_alignFrom, _alignTo, amount);
        if (_alignT >= 1)
        {
            _pose = _alignTo;
            _aligning = false;
        }
    }

    private void EndGesture(bool startInertia)
    {
        _inertiaActive = startInertia && _dragLatched && _velocitySpeed > 0.05;
        if (!_inertiaActive)
        {
            _velocitySpeed = 0;
        }

        _dragLatched = false;
        _gestureDown = false;
        _armOpen = false;
    }

    private InteractionMode ResolveMode(SphereFrameInput input)
    {
        if (!input.WindowInteractive)
        {
            return InteractionMode.Suspended;
        }

        if (input.MenuActive)
        {
            return InteractionMode.MenuOpen;
        }

        if (input.SearchActive)
        {
            return InteractionMode.SearchFocus;
        }

        if (_dragLatched && input.PrimaryDown)
        {
            return InteractionMode.Dragging;
        }

        if (_inertiaActive)
        {
            return InteractionMode.Inertia;
        }

        if (input.PointerOverScene || !input.AutoRotateEnabled || input.ReducedMotion)
        {
            return InteractionMode.HoverPaused;
        }

        return InteractionMode.IdleAutoRotate;
    }
}
