using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;
using RailwayManager.Personnel.Workflows;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-10: Placeholder wizualny pracownika — kapsula (URP Unlit) + kolor per rola + floating label.
    ///
    /// Animacja chodzenia:
    /// - Vertical bob (sine wave ±5cm @ 6Hz podczas walk, ±2cm @ 1Hz podczas idle)
    /// - Rotacja wg kierunku ruchu (LookRotation)
    ///
    /// LOD (D26, M8-10 MVP):
    /// - Pelen render gdy distance &lt; 50m od kamery
    /// - Label-only (ukryj mesh) gdy distance 50-100m
    /// - Disabled gdy &gt; 100m
    ///
    /// Swap na real prefab (M-Models): zastep <see cref="Create"/> metoda ktora instancjuje
    /// rigged prefab z Humanoid animatorem — kontroler <see cref="EmployeeVisual"/> pozostaje.
    /// </summary>
    public class EmployeeVisual : MonoBehaviour
    {
        public int employeeId;
        public EmployeeRole role;

        // ── Walk state (polyline-aware, TD-025) ──
        readonly List<Vector3> _polyline = new();
        int _polylineIndex;           // current target segment end
        bool _walking;
        Action _onArrive;
        float _bobPhase;
        bool _hurry;

        // ── Work animation state (TD-025) ──
        bool _workingAnim;            // when true: subtle rotation sway + small bob
        float _workPhase;

        Renderer _mainRenderer;
        Color _roleColor = Color.gray; // TD-034: bazowy kolor roli (= ubranie robocze)
        GameObject _labelGo;
        TextMesh _labelTextMesh;
        Renderer _labelRenderer;

        const float BaseY = 0.9f; // srodek capsule 1.8m
        const float WalkBobAmplitude = 0.05f;
        const float IdleBobAmplitude = 0.02f;
        const float WorkBobAmplitude = 0.03f;
        const float WalkBobFreq = 6f;
        const float IdleBobFreq = 1f;
        const float WorkBobFreq = 3f;
        const float ArriveThreshold = 0.5f;

        const float LodFullRenderDistance = 50f;
        const float LodLabelOnlyDistance = 100f;

        public static EmployeeVisual Create(int employeeId, Vector3 pos, Transform parent)
        {
            var emp = PersonnelService.GetById(employeeId);
            if (emp == null) return null;

            // Capsule mesh
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"EmployeeVisual_{employeeId}_{emp.DisplayShortName}";
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(pos.x, BaseY, pos.z);
            go.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f); // ~1.8m wysokosci

            // Remove collider (unikamy physics)
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Material: kolor per rola
            var renderer = go.GetComponent<Renderer>();
            var mat = MaterialFactory.CreateUnlit();
            MaterialFactory.SetBaseColor(mat, ColorFromRgb(RoleDefinitions.GetCapsuleColorRgb(emp.role)));
            renderer.material = mat;

            // Component
            var v = go.AddComponent<EmployeeVisual>();
            v.employeeId = employeeId;
            v.role = emp.role;
            v._mainRenderer = renderer;
            v._roleColor = mat.color;
            v.BuildLabel(emp);

            // TD-034: operacyjni (Mechanic/Driver/Conductor/Cleaner/WashBay) zaczynają w ubraniu prywatnym
            // (stonowany tint) dopóki nie przebiorą się przy szafce; biurowi zawsze "w mundurze".
            v.SetWorkClothes(!ScheduledNeedProvider.RoleNeedsWorkClothes(emp.role) || emp.wearingWorkClothes);

            return v;
        }

        void BuildLabel(Employee emp)
        {
            _labelGo = new GameObject("Label");
            _labelGo.transform.SetParent(transform, false);
            _labelGo.transform.localPosition = new Vector3(0, 1.4f, 0);
            _labelGo.transform.localScale = Vector3.one * 0.15f; // TextMesh wymaga skali

            _labelTextMesh = _labelGo.AddComponent<TextMesh>();
            _labelTextMesh.text = $"{emp.DisplayShortName}\n{RoleDefinitions.GetDisplayNamePl(emp.role)}";
            _labelTextMesh.fontSize = 32;
            _labelTextMesh.anchor = TextAnchor.MiddleCenter;
            _labelTextMesh.alignment = TextAlignment.Center;
            _labelTextMesh.color = Color.white;
            _labelTextMesh.characterSize = 0.1f;

            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) _labelTextMesh.font = f;

            _labelRenderer = _labelGo.GetComponent<Renderer>();
            if (_labelRenderer != null && _labelTextMesh.font != null)
            {
                _labelRenderer.material = _labelTextMesh.font.material;
            }

            _labelGo.AddComponent<BillboardFacing>();
        }

        /// <summary>
        /// TD-025: Backwards compat — single-waypoint walk (straight-line do target).
        /// Delegacja do polyline overload z 2-elementową listą.
        /// </summary>
        public void StartWalk(Vector3 target, Action onArrive, bool hurry = false)
        {
            var poly = new List<Vector3> { transform.position, target };
            StartWalk(poly, onArrive, hurry);
        }

        /// <summary>
        /// TD-025: Polyline walk (PathGraph integration). Interpoluje po segmentach
        /// kolejno aż dotrze do ostatniego waypoint'a. Y zerowane do BaseY (capsule center).
        /// </summary>
        public void StartWalk(List<Vector3> polyline, Action onArrive, bool hurry = false)
        {
            _polyline.Clear();
            if (polyline == null || polyline.Count == 0)
            {
                onArrive?.Invoke();
                return;
            }
            // Normalize Y do BaseY (capsule center) bez modyfikacji oryginalnej listy
            for (int i = 0; i < polyline.Count; i++)
            {
                var p = polyline[i];
                _polyline.Add(new Vector3(p.x, BaseY, p.z));
            }
            _polylineIndex = 1; // pierwszy "next target" to drugi waypoint (idx 0 = start)
            // Snap do start waypoint'a
            transform.position = _polyline[0];
            _walking = _polyline.Count > 1;
            _onArrive = onArrive;
            _hurry = hurry;
            _workingAnim = false; // walk wyłącza working animation
            if (!_walking)
            {
                var cb = _onArrive;
                _onArrive = null;
                cb?.Invoke();
            }
        }

        public bool IsWalking => _walking;

        /// <summary>
        /// TD-025: Włącz / wyłącz animację "pracy" (subtle rotation sway + smaller bob).
        /// W trybie working capsule nie chodzi, ale wykonuje gentle gesture żeby było
        /// widać że "pracuje" vs "stoi idle". Placeholder do M-Models swap.
        /// </summary>
        public void SetWorkingAnim(bool active)
        {
            _workingAnim = active;
            if (active) _workPhase = 0f;
        }

        /// <summary>
        /// TD-025: Hide/show capsule (np. Driver embed w pociągu — kapsuła znika
        /// dopóki pociąg jedzie, reappear gdy wraca do depot).
        /// </summary>
        public void SetHidden(bool hidden)
        {
            if (gameObject.activeSelf == !hidden) return;
            gameObject.SetActive(!hidden);
        }

        /// <summary>
        /// TD-034: tint ubrania — work=true → kolor roli (robocze), false → stonowany szary (prywatne).
        /// Placeholder do M-Models (wtedy swap mesha/materiału munduru). Operacyjne role przełączają to
        /// przy szafce (LockerIn/LockerOut); biurowi zawsze work=true.
        /// </summary>
        public void SetWorkClothes(bool work)
        {
            if (_mainRenderer == null) return;
            _mainRenderer.material.color = work
                ? _roleColor
                : Color.Lerp(_roleColor, new Color(0.55f, 0.55f, 0.60f), 0.6f);
        }

        public void Tick(float dt)
        {
            // LOD check
            UpdateLod();

            if (!_walking)
            {
                // Idle vs Working animation
                if (_workingAnim)
                {
                    _workPhase += dt;
                    // Subtle rotation sway ±15° co ~2s + mini bob
                    float swayY = Mathf.Sin(_workPhase * Mathf.PI) * 15f; // 1s period left-right
                    transform.rotation = Quaternion.Euler(0f, swayY, 0f);
                    _bobPhase += dt * WorkBobFreq;
                    ApplyBob(WorkBobAmplitude);
                }
                else
                {
                    _bobPhase += dt * IdleBobFreq;
                    ApplyBob(IdleBobAmplitude);
                }
                return;
            }

            // Walking — interpolate along polyline segments
            Vector3 pos = transform.position;
            Vector3 segmentEnd = _polyline[_polylineIndex];
            Vector3 toSegEnd = segmentEnd - pos;
            toSegEnd.y = 0f;
            float distSq = toSegEnd.sqrMagnitude;

            if (distSq <= ArriveThreshold * ArriveThreshold)
            {
                // Reached this segment end — advance to next or finish
                _polylineIndex++;
                if (_polylineIndex >= _polyline.Count)
                {
                    _walking = false;
                    var cb = _onArrive;
                    _onArrive = null;
                    cb?.Invoke();
                    return;
                }
                // Continue with next segment in same tick (= no frame lag at waypoints)
                segmentEnd = _polyline[_polylineIndex];
                toSegEnd = segmentEnd - pos;
                toSegEnd.y = 0f;
            }

            float speed = _hurry
                ? PersonnelBalanceConstants.WalkSpeedHurryMps
                : PersonnelBalanceConstants.WalkSpeedNormalMps;

            Vector3 dir = toSegEnd.normalized;
            Vector3 step = dir * speed * dt;

            // TD-033 G: yield przed pociągiem — gdy następny krok wszedłby w pas zajętego toru, czekaj
            // (level-crossing; trasa statyczna, blokada dynamiczna). Brak nav-service → ruch normalny.
            var nav = DepotSystem.Nav.DepotNavService.Instance;
            if (nav == null || !nav.IsBlockedByConsist(transform.position + step))
                transform.position += step;

            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    dt * 8f);
            }

            _bobPhase += dt * WalkBobFreq;
            ApplyBob(WalkBobAmplitude);
        }

        void ApplyBob(float amplitude)
        {
            var pos = transform.position;
            pos.y = BaseY + Mathf.Sin(_bobPhase) * amplitude;
            transform.position = pos;
        }

        void UpdateLod()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float sqrDist = (transform.position - cam.transform.position).sqrMagnitude;

            if (sqrDist > LodLabelOnlyDistance * LodLabelOnlyDistance)
            {
                if (_mainRenderer != null && _mainRenderer.enabled) _mainRenderer.enabled = false;
                if (_labelRenderer != null && _labelRenderer.enabled) _labelRenderer.enabled = false;
            }
            else if (sqrDist > LodFullRenderDistance * LodFullRenderDistance)
            {
                if (_mainRenderer != null && _mainRenderer.enabled) _mainRenderer.enabled = false;
                if (_labelRenderer != null && !_labelRenderer.enabled) _labelRenderer.enabled = true;
            }
            else
            {
                if (_mainRenderer != null && !_mainRenderer.enabled) _mainRenderer.enabled = true;
                if (_labelRenderer != null && !_labelRenderer.enabled) _labelRenderer.enabled = true;
            }
        }

        /// <summary>Update labelu gdy zmienił się status pracownika (np. Sick).</summary>
        public void RefreshLabel()
        {
            var emp = PersonnelService.GetById(employeeId);
            if (emp == null || _labelTextMesh == null) return;
            string statusHint = emp.status switch
            {
                EmployeeStatus.Sick => " [L4]",
                EmployeeStatus.OnShift => "",
                EmployeeStatus.Resting => " [wolne]",
                _ => $" [{emp.status}]"
            };
            _labelTextMesh.text = $"{emp.DisplayShortName}\n{RoleDefinitions.GetDisplayNamePl(emp.role)}{statusHint}";
        }

        static Color ColorFromRgb(uint rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }
    }

    /// <summary>
    /// M8-10: Simple billboard — label zawsze face'uje kamere (Camera.main).
    /// Wydajnosciowo OK dla &lt;500 postaci (Update per frame); post-EA LODGroup.
    /// </summary>
    public class BillboardFacing : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.forward = cam.transform.forward;
        }
    }
}
