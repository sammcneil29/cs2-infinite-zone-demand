using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Reflection helpers for the demand systems' private fields. A missing field is reported once at startup
    /// and comes back as null, so callers skip just that part instead of crashing.
    /// </summary>
    internal static class GameFields
    {
        private const BindingFlags kFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static FieldInfo Find(Type type, string name, List<string> missing)
        {
            FieldInfo field = type.GetField(name, kFieldFlags);
            if (field == null)
            {
                missing.Add($"{type.Name}.{name}");
            }
            return field;
        }

        public static void ReportMissing(string owner, List<string> missing)
        {
            if (missing.Count > 0)
            {
                Mod.log.Warn($"{owner}: game fields not found (a game update probably renamed them); the related demand will not be forced: {string.Join(", ", missing)}");
            }
        }

        /// <summary>Completes a JobHandle stored in a private field, e.g. a demand system's m_ReadDependencies.</summary>
        public static void Complete(FieldInfo handleField, object system)
        {
            if (handleField?.GetValue(system) is JobHandle handle)
            {
                handle.Complete();
            }
        }

        public static T GetNative<T>(FieldInfo field, object system) where T : unmanaged
        {
            return ((NativeValue<T>)field.GetValue(system)).value;
        }

        public static void SetNative<T>(FieldInfo field, object system, T value) where T : unmanaged
        {
            // NativeValue is a handle to native memory, so writing through the copy updates the system's value;
            // storing the copy back as well keeps this correct even if that ever changes.
            var nativeValue = (NativeValue<T>)field.GetValue(system);
            nativeValue.value = value;
            field.SetValue(system, nativeValue);
        }

        /// <summary>Raises a plain int field (e.g. an m_Last* cache) to at least <paramref name="minimum"/>.</summary>
        public static void RaiseInt(FieldInfo field, object system, int minimum)
        {
            if (field != null && (int)field.GetValue(system) < minimum)
            {
                field.SetValue(system, minimum);
            }
        }

        public static void Raise(NativeArray<int> values, List<int> indexes, int minimum)
        {
            if (!values.IsCreated)
            {
                return;
            }

            foreach (int i in indexes)
            {
                if (i >= 0 && i < values.Length)
                {
                    values[i] = math.max(values[i], minimum);
                }
            }
        }

        public static void RaiseAll(NativeArray<int> values, int minimum)
        {
            if (!values.IsCreated)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = math.max(values[i], minimum);
            }
        }
    }
}
