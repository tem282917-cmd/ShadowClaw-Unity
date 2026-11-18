using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    public static class Chapter01NativeClock
    {
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long value);
        public static readonly bool Native = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        public static readonly long Frequency = ReadFrequency();
        public static string Backend => Native ? "Windows QueryPerformanceCounter (same system origin as FrameTiming)" : "Stopwatch (origin not assumed compatible with FrameTiming)";
        static long ReadFrequency() { if (Native && QueryPerformanceFrequency(out var value)) return value; return Stopwatch.Frequency; }
        public static long Timestamp() { if (Native && QueryPerformanceCounter(out var value)) return value; return Stopwatch.GetTimestamp(); }
    }
}
