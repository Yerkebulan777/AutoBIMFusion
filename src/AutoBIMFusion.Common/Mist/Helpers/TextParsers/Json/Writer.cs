using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AutoBIMFusion.Common.Mist.Helpers.TextParsers.Json;

//Really simple JSON writer
//- Outputs JSON structures from an object
//- Really simple API (new List<int> { 1, 2, 3 }).ToJson() == "[1,2,3]"
//- Will only output public fields and property getters on objects
public static class Writer
{
    public static string ToJson(this object item)
    {
        StringBuilder stringBuilder = new();
        AppendValue(stringBuilder, item);
        return stringBuilder.ToString();
    }

    private static void AppendValue(StringBuilder stringBuilder, object item)
    {
        if (item == null)
        {
            _ = stringBuilder.Append("null");
            return;
        }

        Type type = item.GetType();
        if (type == typeof(string))
        {
            _ = stringBuilder.Append('"');
            var str = (string)item;
            for (var i = 0; i < str.Length; ++i)
            {
                if (str[i] is < ' ' or '"' or '\\')
                {
                    _ = stringBuilder.Append('\\');
                    var j = "\"\\\n\r\t\b\f".IndexOf(str[i]);
                    _ = j >= 0 ? stringBuilder.Append("\"\\nrtbf"[j]) : stringBuilder.AppendFormat("u{0:X4}", (uint)str[i]);
                }
                else
                {
                    _ = stringBuilder.Append(str[i]);
                }
            }

            _ = stringBuilder.Append('"');
        }
        else if (type == typeof(byte) || type == typeof(int))
        {
            _ = stringBuilder.Append(item);
        }
        else if (type == typeof(float))
        {
            _ = stringBuilder.Append(((float)item).ToString(CultureInfo.InvariantCulture));
        }
        else if (type == typeof(double))
        {
            _ = stringBuilder.Append(((double)item).ToString(CultureInfo.InvariantCulture));
        }
        else if (type == typeof(bool))
        {
            _ = stringBuilder.Append((bool)item ? "true" : "false");
        }
        else if (item is IList)
        {
            _ = stringBuilder.Append('[');
            var isFirst = true;
            IList? list = item as IList;
            for (var i = 0; i < list.Count; i++)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    _ = stringBuilder.Append(',');
                }

                AppendValue(stringBuilder, list[i]);
            }

            _ = stringBuilder.Append(']');
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            Type keyType = type.GetGenericArguments()[0];

            if (keyType.IsEnum)
            {
                //continue
            }
            else if (keyType != typeof(string))
            {
                //Refuse to output dictionary keys that aren't of type string
                _ = stringBuilder.Append("{}");
                return;
            }

            _ = stringBuilder.Append('{');
            IDictionary? dict = item as IDictionary;
            var isFirst = true;
            foreach (var key in dict.Keys)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    _ = stringBuilder.Append(',');
                }

                _ = stringBuilder.Append('\"');
                _ = stringBuilder.Append(key);
                _ = stringBuilder.Append("\":");
                AppendValue(stringBuilder, dict[key]);
            }

            _ = stringBuilder.Append('}');
        }
        else
        {
            _ = stringBuilder.Append('{');

            var isFirst = true;
            FieldInfo[] fieldInfos = type.GetFields();
            for (var i = 0; i < fieldInfos.Length; i++)
            {
                if (fieldInfos[i].IsPublic && !fieldInfos[i].IsStatic)
                {
                    var value = fieldInfos[i].GetValue(item);
                    if (value != null)
                    {
                        if (isFirst)
                        {
                            isFirst = false;
                        }
                        else
                        {
                            _ = stringBuilder.Append(',');
                        }

                        _ = stringBuilder.Append('\"');
                        _ = stringBuilder.Append(fieldInfos[i].Name);
                        _ = stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }
            }

            PropertyInfo[] propertyInfo = type.GetProperties();
            for (var i = 0; i < propertyInfo.Length; i++)
            {
                if (propertyInfo[i].CanRead)
                {
                    var value = propertyInfo[i].GetValue(item, null);
                    if (value != null)
                    {
                        if (isFirst)
                        {
                            isFirst = false;
                        }
                        else
                        {
                            _ = stringBuilder.Append(',');
                        }

                        _ = stringBuilder.Append('\"');
                        _ = stringBuilder.Append(propertyInfo[i].Name);
                        _ = stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }
            }

            _ = stringBuilder.Append('}');
        }
    }
}
