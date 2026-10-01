using System;
using System.Reflection;
using HarmonyLib;
using NormalGolfMultiplayer.Net;
using TMPro;
using UnityEngine;

namespace NormalGolfMultiplayer.Game
{
    // The game's coroutine and LateUpdate both write wind. Override clients before visuals,
    // after the coroutine, and immediately before physics reads it; the host keeps its normal cycle.
    internal static class WindSync
    {
        private static readonly FieldInfo Text = AccessTools.Field(typeof(WindPanel), "m_text");
        private static readonly FieldInfo TimerText = AccessTools.Field(typeof(WindPanel), "m_timerText");
        private static readonly FieldInfo Lock = AccessTools.Field(typeof(WindPanel), "m_lock");
        private static WindPanel _overriddenPanel;
        private static float _originalForce;

        public static bool Apply(WindPanel panel)
        {
            var session = NetSession.Instance;
            if (panel == null || session == null || !session.InSession || session.IsHost || !session.HasWind)
            {
                if (_overriddenPanel != null)
                    _overriddenPanel.m_forceMutiplierOfWindOnBall = _originalForce;
                _overriddenPanel = null;
                return false;
            }
            if (_overriddenPanel != panel)
            {
                _overriddenPanel = panel;
                _originalForce = panel.m_forceMutiplierOfWindOnBall;
            }
            var wind = session.CurrentWind;
            panel.m_currentWind = new Vector2(wind.X, wind.Y);
            panel.m_forceMutiplierOfWindOnBall = wind.ForceMultiplier;
            return true;
        }

        public static void BeforeVisuals(WindPanel __instance) => Apply(__instance);
        public static void BeforePhysics()
        {
            if (PanelManager.instance != null)
                Apply(PanelManager.instance.m_windPanel);
        }

        public static bool BeforeLateUpdate(WindPanel __instance)
        {
            if (!Apply(__instance))
                return true;
            var wind = NetSession.Instance.CurrentWind;
            ((TMP_Text)Text.GetValue(__instance)).text =
                (__instance.m_currentWind.magnitude * 2.5f).ToString("N0") + "<size=18> mph";
            var timer = (TMP_Text)TimerText.GetValue(__instance);
            timer.transform.parent.gameObject.SetActive(wind.CalmSeconds > 0f);
            if (wind.CalmSeconds > 0f)
            {
                var remaining = TimeSpan.FromSeconds(wind.CalmSeconds);
                timer.text = string.Format("{0:00}:{1:00}", remaining.Minutes, remaining.Seconds);
            }
            ((GameObject)Lock.GetValue(__instance)).SetActive(wind.Locked);
            return false; // Client challenges/items must not replace the host's wind.
        }

        public static void AfterLateUpdate(WindPanel __instance)
        {
            var session = NetSession.Instance;
            if (session == null || !session.IsHost)
                return;
            var run = SaveManager.instance != null ? SaveManager.instance.m_run : default;
            session.PublishWind(new WindState
            {
                X = __instance.m_currentWind.x,
                Y = __instance.m_currentWind.y,
                ForceMultiplier = __instance.m_forceMutiplierOfWindOnBall,
                CalmSeconds = run.isInLMUGC ? Mathf.Max(0f, run.lmugcNoWindTime) : 0f,
                Locked = ((GameObject)Lock.GetValue(__instance)).activeSelf,
            });
        }
    }
}
