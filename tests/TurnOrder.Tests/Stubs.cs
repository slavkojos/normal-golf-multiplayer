// Only engine services are replaced. The real turn, protocol and session sources run under test.
using System;
using NormalGolfMultiplayer.Net;
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Time
    {
        public static float unscaledTime;
        public static double realtimeSinceStartupAsDouble => unscaledTime;
    }
    public static class Application { public static bool runInBackground; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r=r; this.g=g; this.b=b; this.a=a; }
    }
    public static class Mathf
    {
        public static float Abs(float f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int Clamp(int f, int min, int max) => Math.Clamp(f, min, max);
    }
}
namespace NormalGolfMultiplayer
{
    internal static class Plugin
    {
        public static readonly Logger Log = new Logger();
        internal sealed class Logger
        {
            public void LogInfo(string s) { }
            public void LogWarning(string s) { }
        }
    }
    internal static class DevTools { public static bool LoopbackOnly => true; }
    internal static class ModConfig
    {
        internal sealed class Setting<T> { public T Value; }
        public static readonly Setting<string> Password = new Setting<string> { Value = "" };
        public static readonly Setting<int> MaxPlayers = new Setting<int> { Value = 8 };
        public static string ResolveName() => "Test";
        public static UnityEngine.Color32 GetColor() => new UnityEngine.Color32(255,255,255,255);
    }
}
namespace NormalGolfMultiplayer.Game
{
    internal static class LocalPlayer
    {
        public static bool ShotPending;
        public static bool HasHole = true;
        public static UnityEngine.Vector3 Hole;
        public static bool TryGetHolePosition(UnityEngine.Vector3 position, out UnityEngine.Vector3 hole) { hole = Hole; return HasHole; }
        public static PlayerState Capture() => default;
        public static byte CurrentBallLook() => 0;
    }
}

public enum ShotType : byte { Topped = 1, Thin = 2, Good = 4, Fat = 8, Ground = 16, Perfect = 64 }
public enum SpinType : byte { None = 0, Straight = 1, Slice = 2, Hook = 4, Draw = 8, Fade = 16 }
public sealed class TestBody { public UnityEngine.Vector3 position; }
public sealed class Ball {
    public TestBody m_rb = new TestBody();
    public TestBody transform = new TestBody();
    public int m_currentShot;
}
public sealed class HitManager {
    public static HitManager instance = new HitManager();
    public Ball m_ball = new Ball();
    public SpinType TestSpin = SpinType.Straight;
    public SpinType GetSpinType() => TestSpin;
}
