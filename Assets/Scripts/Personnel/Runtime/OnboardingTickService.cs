using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-6 / MM-D11 — per-frame tick processor przesuwający pracowników z
    /// <see cref="EmployeeStatus.Onboarding"/> do <see cref="EmployeeStatus.OnShift"/>
    /// gdy minął <see cref="Employee.onboardingFinishGameTime"/>.
    ///
    /// Dlaczego per-frame, nie daily: onboarding to in-game minutes (7.5-30min wg
    /// Dispatcher lvl). Przy x150 game speed 30 min gry = 12 sek real-time.
    /// PersonnelDailyScheduler tick jest na koniec dnia gracza (~24h gry =
    /// ~9.6 min real @ x150), za rzadko.
    ///
    /// Throttle: check co N=2 frame'ów żeby zaoszczędzić cykli (precyzja Onboarding
    /// jest na poziomie minut, nie milisekund).
    /// </summary>
    public class OnboardingTickService : MonoBehaviour
    {
        public static OnboardingTickService Instance { get; private set; }

        private const int CheckEveryNFrames = 2;
        private int _frameCounter;

        public static OnboardingTickService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("OnboardingTickService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<OnboardingTickService>();
            return Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            EnsureExists();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            _frameCounter++;
            if (_frameCounter < CheckEveryNFrames) return;
            _frameCounter = 0;

            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            int promoted = 0;

            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                if (e.status != EmployeeStatus.Onboarding) continue;
                if (e.onboardingFinishGameTime <= 0L) continue;
                if (now < e.onboardingFinishGameTime) continue;

                e.status = EmployeeStatus.OnShift;
                e.onboardingFinishGameTime = 0L;
                PersonnelService.NotifyStatusChanged(e);
                promoted++;
            }

            if (promoted > 0)
                Log.Debug($"[OnboardingTickService] Promoted {promoted} employee(s) Onboarding → OnShift");
        }

        [ContextMenu("Debug: Force promote all Onboarding → OnShift")]
        public void DebugForcePromote()
        {
            int n = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.status != EmployeeStatus.Onboarding) continue;
                e.status = EmployeeStatus.OnShift;
                e.onboardingFinishGameTime = 0L;
                PersonnelService.NotifyStatusChanged(e);
                n++;
            }
            Log.Info($"[OnboardingTickService] Force-promoted {n} employee(s)");
        }
    }
}
