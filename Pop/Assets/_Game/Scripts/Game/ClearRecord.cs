using UnityEngine;

namespace Pop.Game
{
    /// <summary>
    /// 클리어 시간 기록 (PlayerPrefs). 최고 기록 = 가장 짧은 시간(초).
    /// </summary>
    public static class ClearRecord
    {
        public const string BestKey = "Pop.BestClearTime";

        /// <summary>저장된 최고 기록이 있는가</summary>
        public static bool HasBest => PlayerPrefs.HasKey(BestKey) && PlayerPrefs.GetFloat(BestKey) > 0f;

        /// <summary>최고 기록(초). 없으면 -1</summary>
        public static float Best => HasBest ? PlayerPrefs.GetFloat(BestKey) : -1f;

        /// <summary>이번 기록을 내고, 최고 기록을 깼으면 저장하고 true.</summary>
        public static bool Submit(float seconds)
        {
            if (seconds <= 0f) return false;
            if (HasBest && seconds >= Best) return false;
            PlayerPrefs.SetFloat(BestKey, seconds);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>최고 기록 지우기 (개발용)</summary>
        public static void ClearBest()
        {
            PlayerPrefs.DeleteKey(BestKey);
            PlayerPrefs.Save();
        }

        /// <summary>mm:ss.ff (분은 100 넘어도 그대로)</summary>
        public static string Format(float seconds)
        {
            if (seconds < 0f) return "--:--.--";
            int cs = Mathf.FloorToInt(seconds * 100f);
            int m = cs / 6000;
            int s = (cs / 100) % 60;
            int f = cs % 100;
            return $"{m:00}:{s:00}.{f:00}";
        }

        /// <summary>최고 기록 문자열. 없으면 "—:—"</summary>
        public static string BestText => HasBest ? Format(Best) : "—:—";
    }
}
