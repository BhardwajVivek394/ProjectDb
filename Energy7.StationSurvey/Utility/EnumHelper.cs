using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using static Energy7.StationSurvey.Utility.Utility;

namespace Energy7.StationSurvey.Utility
{
    public static class EnumHelper
    {
        public static T GetEnumValue<T>(string str) where T : struct, IConvertible
        {
            if (!typeof(T).IsEnum) throw new Exception("T must be an Enumeration type.");
            return Enum.TryParse<T>(str, true, out T val) ? val : default;
        }

        public static T GetEnumString<T>(int intValue) where T : struct, IConvertible
        {
            if (!typeof(T).IsEnum) throw new Exception("T must be an Enumeration type.");
            return (T)Enum.ToObject(typeof(T), intValue);
        }

        public static string GetDescription<T>(int intValue) where T : struct, IConvertible
        {
            try
            {
                if (!typeof(T).IsEnum) throw new Exception("T must be an Enumeration type.");
                var enumValue = Enum.ToObject(typeof(T), intValue);
                var fieldInfo = enumValue.GetType().GetField(enumValue.ToString()!);
                if (fieldInfo != null)
                {
                    var attrs = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), true);
                    if (attrs.Length > 0) return ((DescriptionAttribute)attrs[0]).Description;
                }
            }
            catch { }
            return string.Empty;
        }

        public static List<string> GetEnumDisplayNames(Enum enm)
        {
            var type = enm.GetType();
            var displayNames = new List<string>();
            foreach (var name in Enum.GetNames(type))
            {
                var field    = type.GetField(name)!;
                var objArray = field.GetCustomAttributes(typeof(DisplayAttribute), true);
                if (objArray.Length == 0) displayNames.Add(name);
                foreach (DisplayAttribute fd in objArray) displayNames.Add(fd.Name ?? name);
            }
            return displayNames;
        }

        public static string GetDisplayName(Enum enumValue)
            => enumValue.GetType()
                        .GetMember(enumValue.ToString())
                        .First()
                        .GetCustomAttribute<DisplayAttribute>()
                        ?.GetName() ?? enumValue.ToString();

        public static string GetEnumString(ThickWaveType statusType) => statusType switch
        {
            ThickWaveType.A    => nameof(ThickWaveType.A),
            ThickWaveType.B    => nameof(ThickWaveType.B),
            ThickWaveType.Both => nameof(ThickWaveType.Both),
            _                  => throw new ArgumentOutOfRangeException(nameof(statusType))
        };

        public static string GetEnumString(ClasificationTypes statusType) => statusType switch
        {
            ClasificationTypes.Image => nameof(ClasificationTypes.Image),
            ClasificationTypes.Array => nameof(ClasificationTypes.Array),
            _                        => throw new ArgumentOutOfRangeException(nameof(statusType))
        };

        public static string GetDisplayNameById<TEnum>(int id) where TEnum : Enum
        {
            var enumValue  = (TEnum)Enum.ToObject(typeof(TEnum), id);
            var memberInfo = typeof(TEnum).GetMember(enumValue.ToString()).FirstOrDefault();
            return memberInfo?.GetCustomAttribute<DisplayAttribute>()?.Name ?? enumValue.ToString();
        }

        public static string GetDisplayName<TEnum>(int value) where TEnum : Enum
        {
            var enumValue = (TEnum)Enum.ToObject(typeof(TEnum), value);
            var member    = typeof(TEnum).GetMember(enumValue.ToString()).FirstOrDefault();
            return member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? enumValue.ToString();
        }

        /// <summary>Returns SelectListItem list — use directly with asp-items in Razor.</summary>
        public static IEnumerable<SelectListItem> GetEnumSelectList<TEnum>() where TEnum : Enum
        {
            var type = typeof(TEnum);
            return Enum.GetValues(type).Cast<TEnum>().Select(e =>
            {
                var member      = type.GetMember(e.ToString()).First();
                var displayAttr = member.GetCustomAttribute<DisplayAttribute>();
                return new SelectListItem
                {
                    Text  = displayAttr?.Name ?? e.ToString(),
                    Value = Convert.ToInt32(e).ToString()
                };
            });
        }
    }
}
