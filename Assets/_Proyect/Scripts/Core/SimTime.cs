using System;
using UnityEngine;

namespace Tsunami.Core
{
    /// <summary>
    /// Utilidades para formatear tiempos de simulación.
    /// Recibe segundos y devuelve strings legibles: mm:ss, hh:mm:ss, d:hh:mm:ss.
    /// </summary>
    public static class SimTime
    {
        /// <summary>
        /// Formatea segundos a:
        ///   - "mm:ss" si &lt; 1h,
        ///   - "hh:mm:ss" si &lt; 1 día,
        ///   - "d:hh:mm:ss" si ≥ 1 día.
        /// </summary>
        public static string FormatAuto(int seconds)
        {
            if (seconds < 0) seconds = 0;

            var ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalDays >= 1.0)
            {
                // d:hh:mm:ss (sin ceros a la izquierda en días)
                return $"{(int)ts.TotalDays}:{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            }

            if (ts.TotalHours >= 1.0)
            {
                // hh:mm:ss
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            }

            // mm:ss
            return $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        /// <summary>
        /// Igual a FormatAuto pero siempre muestra horas: "hh:mm:ss" (útil para UI consistente).
        /// </summary>
        public static string FormatHMS(int seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        /// <summary>
        /// Convierte un valor en una unidad dada a segundos (por si algún simulador cambiara de unidad).
        /// </summary>
        public static int ToSeconds(double value, TimeUnit unit)
        {
            switch (unit)
            {
                case TimeUnit.Seconds: return (int)Math.Round(value);
                case TimeUnit.Minutes: return (int)Math.Round(value * 60.0);
                case TimeUnit.Hours:   return (int)Math.Round(value * 3600.0);
                default:               return (int)Math.Round(value);
            }
        }
    }

    public enum TimeUnit { Seconds, Minutes, Hours }
}
