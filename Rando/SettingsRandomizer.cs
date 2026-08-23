using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MenuChanger.Attributes;

namespace ConnectionSettingsRando
{
    public class RandomizationStats
    {
        public List<string> RandomizedMembers = [];
        public List<string> EnforcedMembers = [];
        public List<string> SkippedMembers = [];
        public int RandomizedCount { get; set; }
        public int EnforcedCount { get; set; }
        public int SkippedCount { get; set; }
    }
    public class SettingsRandomizer
    {
        private RandomizationStats stats = new();
        public (T, RandomizationStats) Randomize<T>(T settings, Random rng, string providerName)
            where T : new()
        {
            stats = new();
            return ((T)RandomizeObject(settings, rng, [providerName]), stats);
        }
        public static bool Skip(Type memberType, string memberName, IReadOnlyList<string> path)
        {
            string name = string.Join(".", path.Append(memberName));
            if (RandoInterop.OptOutManager.GetRule(name) is OptOutRule rule && rule.Action == OptOutAction.Exclude)
                return true;
            else if (memberType == typeof(bool))
                return !RandoInterop.Settings.IncludeBooleans;
            else if (IsNumeric(memberType))
                return !RandoInterop.Settings.IncludeNumeric;
            else if (memberType.IsEnum)
                return !RandoInterop.Settings.IncludeCategorical;
            else
                return false;
        }

        public static bool Skip(MemberInfo member, IReadOnlyList<string> path) => Skip(GetMemberType(member), member.Name, path);
        private object RandomizeValue(MemberInfo member, object value, Random rng, IReadOnlyList<string> path)
        {
            if (Skip(member, path))
            {
                TrackSkip(member.Name, path);
                return value;
            }

            // For attributes with CSRIgnore, the mod doesn't keep track or log it as skipped or randomized and assumes the setting doesn't exist.
            if (member.GetCustomAttributes().Any(attr => attr.GetType().Name == "CSRIgnoreAttribute"))
                return value;

            Type type = GetMemberType(member);
            if (type == typeof(bool))
                return RandomizeBool(member, path, rng);
            else if (IsNumeric(type))
            {
                TrackRando(member.Name, path);
                return RandomizeNumeric(member, rng);
            }
            else if (type.IsEnum)
            {
                TrackRando(member.Name, path);
                return RandomizeEnum(type, rng);
            }
            else if (!type.IsPrimitive && value != null)
            {
                IReadOnlyList<string> nestedPath = [.. path, member.Name];
                return RandomizeObject(value, rng, nestedPath);
            }
            else
            {
                TrackSkip(member.Name, path);
                return value;
            }
        }
        public static void CopyTo<T>(T source, T destination)
        {
            foreach (MemberInfo member in GetMembers(typeof(T)))
            {
                SetValue(
                    member,
                    destination,
                    GetValue(member, source));
            }
        }
        private object RandomizeBool(MemberInfo member, IReadOnlyList<string> path, Random rng)
        {
            string name = string.Join(".", path.Append(member.Name));
            OptOutRule rule = RandoInterop.OptOutManager.GetRule(name);
            if (rule?.Action == OptOutAction.ForceTrue)
            {
                TrackEnforce(member.Name, path);
                return true;
            }
            if (rule?.Action == OptOutAction.ForceFalse)
            {
                TrackEnforce(member.Name, path);
                return false;
            }
            
            TrackRando(member.Name, path);
            return RandoInterop.Settings.SettingOdds > rng.NextDouble();
        }

        private object RandomizeNumeric(
            MemberInfo member,
            Random rng)
        {
            var range = member.GetCustomAttribute<MenuRangeAttribute>();

            switch (Type.GetTypeCode(GetMemberType(member)))
            {
                case TypeCode.Byte:
                {
                    byte min = Convert.ToByte(range?.min ?? byte.MinValue);
                    byte max = Convert.ToByte(range?.max ?? byte.MaxValue);

                    return (byte)rng.Next(min, max + 1);
                }

                case TypeCode.Int16:
                {
                    short min = Convert.ToInt16(range?.min ?? short.MinValue);
                    short max = Convert.ToInt16(range?.max ?? short.MaxValue);

                    return (short)rng.Next(min, max + 1);
                }

                case TypeCode.Int32:
                {
                    int min = Convert.ToInt32(range?.min ?? int.MinValue);
                    int max = Convert.ToInt32(range?.max ?? int.MaxValue);

                    return rng.Next(min, max + 1);
                }

                case TypeCode.Int64:
                {
                    long min = Convert.ToInt64(range?.min ?? long.MinValue);
                    long max = Convert.ToInt64(range?.max ?? long.MaxValue);

                    // Random doesn't support long directly
                    return min + (long)(rng.NextDouble() * (max - min + 1));
                }

                case TypeCode.Single:
                {
                    float min = Convert.ToSingle(range?.min ?? float.MinValue);
                    float max = Convert.ToSingle(range?.max ?? float.MaxValue);

                    return (float)(min + rng.NextDouble() * (max - min));
                }

                case TypeCode.Double:
                {
                    double min = Convert.ToDouble(range?.min ?? double.MinValue);
                    double max = Convert.ToDouble(range?.max ?? double.MaxValue);

                    return min + rng.NextDouble() * (max - min);
                }

                case TypeCode.Decimal:
                {
                    decimal min = Convert.ToDecimal(range?.min ?? decimal.MinValue);
                    decimal max = Convert.ToDecimal(range?.max ?? decimal.MaxValue);

                    return min + (decimal)rng.NextDouble() * (max - min);
                }

                default:
                    throw new NotSupportedException(
                        $"Unsupported numeric type {GetMemberType(member).Name}");
            }
        }
        private object RandomizeEnum(
            Type enumType,
            Random rng)
        {
            Array values = Enum.GetValues(enumType);
            return values.GetValue(rng.Next(values.Length))!;
        }
        private bool IsSettingsObject(Type type)
        {
            return type.IsClass
                && type != typeof(string)
                && type.GetConstructor(Type.EmptyTypes) != null;
        }
        private object RandomizeObject(object instance, Random rng, IReadOnlyList<string> path)
        {
            Type type = instance.GetType();
            if (!IsSettingsObject(type))
                return instance;

            object clone = Activator.CreateInstance(type)!;
            foreach (MemberInfo member in GetMembers(type))
            {
                object value = GetValue(member, instance);
                object randomized = RandomizeValue(member, value, rng, path);
                SetValue(member, clone, randomized);
            }

            while (!ValidateDynamicBounds(clone))
            {
                foreach (MemberInfo member in GetMembers(type))
                {
                    if (HasInvalidDynamicBounds(clone, member))
                    {
                        object value = GetValue(member, clone);
                        object randomized = RandomizeValue(member, value, rng, path);
                        SetValue(member, clone, randomized);
                    }
                }
            }

            return clone;
        }
        private static bool ValidateDynamicBounds(object settings)
        {
            foreach (MemberInfo member in GetMembers(settings.GetType()))
            {
                DynamicBoundAttribute[] attributes =
                    GetAttributes<DynamicBoundAttribute>(member);

                foreach (DynamicBoundAttribute attribute in attributes)
                {
                    if (!SatisfiesBound(settings, member, attribute))
                        return false;
                }
            }
            return true;
        }

        private static bool HasInvalidDynamicBounds(
            object settings,
            MemberInfo member)
        {
            DynamicBoundAttribute[] attributes =
                GetAttributes<DynamicBoundAttribute>(member);

            foreach (DynamicBoundAttribute attribute in attributes)
            {
                if (!SatisfiesBound(settings, member, attribute))
                    return true;
            }

            return false;
        }

        private static bool SatisfiesBound(
            object settings,
            MemberInfo member,
            DynamicBoundAttribute attribute)
        {
            object value = GetValue(member, settings);
            object boundValue = GetBoundValue(settings, attribute.memberName);

            if (value == null || boundValue == null)
                return true;

            if (value is not IComparable comparableValue)
                return true;

            int comparison = comparableValue.CompareTo(boundValue);

            return attribute.upper
                ? comparison <= 0
                : comparison >= 0;
        }

        private static object GetBoundValue(
            object settings,
            string memberName)
        {
            MemberInfo boundMember =
                GetMembers(settings.GetType())
                    .FirstOrDefault(m => m.Name == memberName);

            if (boundMember == null)
                return null;

            return GetValue(boundMember, settings);
        }

        private static bool IsNumeric(Type type)
        {
            return type == typeof(byte)
                || type == typeof(short)
                || type == typeof(int)
                || type == typeof(long)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal);
        }
        private static IEnumerable<MemberInfo> GetMembers(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.CanWrite)
                    yield return property;
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly)
                    yield return field;
            }
        }
        private static object GetValue(MemberInfo member, object instance)
        {
            return member switch
            {
                PropertyInfo property => property.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => throw new NotSupportedException(
                    $"Unsupported member type {member.MemberType}")
            };
        }

        private static void SetValue(MemberInfo member, object instance, object value)
        {
            switch (member)
            {
                case PropertyInfo property:
                    property.SetValue(instance, value);
                    break;

                case FieldInfo field:
                    field.SetValue(instance, value);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported member type {member.MemberType}");
            }
        }

        private static Type GetMemberType(MemberInfo member)
        {
            return member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => throw new NotSupportedException(
                    $"Unsupported member type {member.MemberType}")
            };
        }

        private static T[] GetAttributes<T>(MemberInfo member)
            where T : Attribute
        {
            return [.. member
                .GetCustomAttributes(typeof(T), true)
                .Cast<T>()];
        }
        private void TrackEnforce(string memberName, IReadOnlyList<string> path)
        {
            string name = string.Join(".", path.Append(memberName));
            if (!stats.EnforcedMembers.Contains(name))
            {
                stats.EnforcedCount++;
                stats.EnforcedMembers.Add(name);
                ConnectionSettingsRando.Instance.Log($"Enforced {name}");
            }
        }
        public static void TrackRando(string memberName, IReadOnlyList<string> path, RandomizationStats stats)
        {
            string name = string.Join(".", path.Append(memberName));
            if (!stats.RandomizedMembers.Contains(name))
            {
                stats.RandomizedCount++;
                stats.RandomizedMembers.Add(name);
                ConnectionSettingsRando.Instance.Log($"Randomized {name}");
            }
        }
        private void TrackRando(string memberName, IReadOnlyList<string> path) => TrackRando(memberName, path, stats);
        public static void TrackSkip(string memberName, IReadOnlyList<string> path, RandomizationStats stats)
        {
            stats.SkippedCount++;
            string name = string.Join(".", path.Append(memberName));
            stats.SkippedMembers.Add(name);
            ConnectionSettingsRando.Instance.Log($"Skipped {name}");
        }
        private void TrackSkip(string memberName, IReadOnlyList<string> path) => TrackSkip(memberName, path, stats);
    }
}