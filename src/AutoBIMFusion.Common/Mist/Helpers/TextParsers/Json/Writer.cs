using System.Collections;
using System.Globalization;
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
        var stringBuilder = new StringBuilder();
        AppendValue(stringBuilder, item);
        return stringBuilder.ToString();
    }

    private static void AppendValue(StringBuilder stringBuilder, object item)
    {
        if (item == null)
        {
            stringBuilder.Append("null");
            return;
        }

        var type = item.GetType();
        if (type == typeof(string))
        {
            stringBuilder.Append('"');
            var str = (string)item;
            for (var i = 0; i < str.Length; ++i)
                if (str[i] < ' ' || str[i] == '"' || str[i] == '\\')
                {
                    stringBuilder.Append('\\');
                    var j = "\"\\\n\r\t\b\f".IndexOf(str[i]);
                    if (j >= 0)
                        stringBuilder.Append("\"\\nrtbf"[j]);
                    else
                        stringBuilder.AppendFormat("u{0:X4}", (uint)str[i]);
                }
                else
                {
                    stringBuilder.Append(str[i]);
                }

            stringBuilder.Append('"');
        }
        else if (type == typeof(byte) || type == typeof(int))
        {
            stringBuilder.Append(item);
        }
        else if (type == typeof(float))
        {
            stringBuilder.Append(((float)item).ToString(CultureInfo.InvariantCulture));
        }
        else if (type == typeof(double))
        {
            stringBuilder.Append(((double)item).ToString(CultureInfo.InvariantCulture));
        }
        else if (type == typeof(bool))
        {
            stringBuilder.Append((bool)item ? "true" : "false");
        }
        else if (item is IList)
        {
            stringBuilder.Append('[');
            var isFirst = true;
            var list = item as IList;
            for (var i = 0; i < list.Count; i++)
            {
                if (isFirst)
                    isFirst = false;
                else
                    stringBuilder.Append(',');

                AppendValue(stringBuilder, list[i]);
            }

            stringBuilder.Append(']');
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = type.GetGenericArguments()[0];

            if (keyType.IsEnum)
            {
                //continue
            }
            else if (keyType != typeof(string))
            {
                //Refuse to output dictionary keys that aren't of type string
                stringBuilder.Append("{}");
                return;
            }

            stringBuilder.Append('{');
            var dict = item as IDictionary;
            var isFirst = true;
            foreach (var key in dict.Keys)
            {
                if (isFirst)
                    isFirst = false;
                else
                    stringBuilder.Append(',');

                stringBuilder.Append('\"');
                stringBuilder.Append(key);
                stringBuilder.Append("\":");
                AppendValue(stringBuilder, dict[key]);
            }

            stringBuilder.Append('}');
        }
        else
        {
            stringBuilder.Append('{');

            var isFirst = true;
            var fieldInfos = type.GetFields();
            for (var i = 0; i < fieldInfos.Length; i++)
                if (fieldInfos[i].IsPublic && !fieldInfos[i].IsStatic)
                {
                    var value = fieldInfos[i].GetValue(item);
                    if (value != null)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            stringBuilder.Append(',');

                        stringBuilder.Append('\"');
                        stringBuilder.Append(fieldInfos[i].Name);
                        stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }

            var propertyInfo = type.GetProperties();
            for (var i = 0; i < propertyInfo.Length; i++)
                if (propertyInfo[i].CanRead)
                {
                    var value = propertyInfo[i].GetValue(item, null);
                    if (value != null)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            stringBuilder.Append(',');

                        stringBuilder.Append('\"');
                        stringBuilder.Append(propertyInfo[i].Name);
                        stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }

            stringBuilder.Append('}');
        }
    }
}
